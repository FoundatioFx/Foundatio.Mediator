using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Mediator.Distributed;

/// <summary>In-process, best-effort pub/sub for development and tests.</summary>
public sealed class InMemoryPubSubClient(ILogger<InMemoryPubSubClient>? logger = null) : IPubSubClient, IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Subscription>> _subscriptions = new();
    private readonly ILogger _logger = logger ?? NullLogger<InMemoryPubSubClient>.Instance;
    private int _disposed;

    /// <summary>
    /// Reports subscriber exceptions without terminating other subscribers. Invoked on the consumer
    /// task, so observers must be thread safe. Message bodies are never included in diagnostics.
    /// </summary>
    public event Action<Exception>? DeliveryFailed;

    public Task PublishAsync(string topic, IReadOnlyList<PubSubEntry> entries, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_subscriptions.TryGetValue(topic, out var subscribers))
            return Task.CompletedTask;
        foreach (var entry in entries)
        {
            var message = new PubSubMessage
            {
                Body = entry.Body,
                Headers = entry.Headers is not null ? new Dictionary<string, string>(entry.Headers) : new Dictionary<string, string>()
            };
            foreach (var subscriber in subscribers.Values)
                subscriber.Writer.TryWrite(message);
        }
        return Task.CompletedTask;
    }

    public Task<IAsyncDisposable> SubscribeAsync(string topic, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var subscribers = _subscriptions.GetOrAdd(topic, _ => new());
        var id = Guid.NewGuid();
        var subscription = new Subscription(this, topic, handler, cancellationToken, () => subscribers.TryRemove(id, out _));
        subscribers.TryAdd(id, subscription);
        subscription.Start();
        if (Volatile.Read(ref _disposed) != 0)
            subscription.Stop();
        return Task.FromResult<IAsyncDisposable>(subscription);
    }

    private void ReportFailure(string topic, Exception exception)
    {
        _logger.LogError(exception, "In-memory pub/sub subscriber failed on {Topic}", topic);
        try { DeliveryFailed?.Invoke(exception); }
        catch (Exception observerError) { _logger.LogWarning(observerError, "Pub/sub diagnostic observer failed"); }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        var subscriptions = _subscriptions.Values.SelectMany(subscribers => subscribers.Values).ToArray();
        await Task.WhenAll(subscriptions.Select(subscription => subscription.DisposeAsync().AsTask())).ConfigureAwait(false);
        _subscriptions.Clear();
    }

    private sealed class Subscription(InMemoryPubSubClient owner, string topic,
        Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken token, Action remove) : IAsyncDisposable
    {
        private readonly Channel<PubSubMessage> _channel = Channel.CreateUnbounded<PubSubMessage>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        private Task _consumer = Task.CompletedTask;
        private int _stopped;
        public ChannelWriter<PubSubMessage> Writer => _channel.Writer;
        public void Start() => _consumer = Task.Run(ConsumeAsync);

        private async Task ConsumeAsync()
        {
            var cancellationToken = _stop.Token;
            try
            {
                await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    try { await handler(message, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                    catch (Exception exception) { owner.ReportFailure(topic, exception); }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            finally { remove(); }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
                return;
            remove();
            Writer.TryComplete();
            _stop.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            Stop();
            try
            {
                await _consumer.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                _stop.Dispose();
            }
            catch (TimeoutException exception)
            {
                owner.ReportFailure(topic, exception);
                _ = _consumer.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
}
