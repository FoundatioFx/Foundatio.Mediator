using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// In-process pub/sub client backed by <see cref="Channel{T}"/>.
/// Useful for testing and single-process scenarios where distributed fan-out
/// collapses to local delivery.
/// </summary>
public sealed class InMemoryPubSubClient : IPubSubClient, IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, SubscriptionEntry>> _subscriptions = new();
    private readonly ConcurrentBag<CancellationTokenSource> _activeCts = [];

    /// <inheritdoc />
    public Task PublishAsync(string topic, IReadOnlyList<PubSubEntry> entries, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.TryGetValue(topic, out var subs))
            return Task.CompletedTask;

        foreach (var entry in entries)
        {
            var message = new PubSubMessage
            {
                Body = entry.Body,
                Headers = entry.Headers is not null
                    ? new Dictionary<string, string>(entry.Headers)
                    : new Dictionary<string, string>()
            };

            foreach (var sub in subs.Values)
                sub.Writer.TryWrite(message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IAsyncDisposable> SubscribeAsync(string topic, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        var entries = _subscriptions.GetOrAdd(topic, _ => new ConcurrentDictionary<Guid, SubscriptionEntry>());

        var channel = Channel.CreateUnbounded<PubSubMessage>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });

        var id = Guid.NewGuid();
        var entry = new SubscriptionEntry(channel.Writer);
        entries.TryAdd(id, entry);

        // Start consumer task that reads from the channel and invokes the handler
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeCts.Add(cts);
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in channel.Reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await handler(msg, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch
                    {
                        // Swallow handler exceptions to keep the subscription alive.
                        // In-memory client is for dev/testing; production transports log errors.
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, cts.Token);

        IAsyncDisposable subscription = new Subscription(() =>
        {
            entries.TryRemove(id, out _);
            channel.Writer.TryComplete();
            cts.Cancel();
            cts.Dispose();
            return ValueTask.CompletedTask;
        });

        return Task.FromResult(subscription);
    }

    public void Dispose()
    {
        // Cancel all active subscription consumer tasks
        foreach (var cts in _activeCts)
        {
            try { cts.Cancel(); cts.Dispose(); } catch { }
        }

        foreach (var topicEntries in _subscriptions.Values)
        {
            foreach (var entry in topicEntries.Values)
                entry.Writer.TryComplete();
            topicEntries.Clear();
        }
        _subscriptions.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    private sealed class SubscriptionEntry(ChannelWriter<PubSubMessage> writer)
    {
        public ChannelWriter<PubSubMessage> Writer => writer;
    }

    private sealed class Subscription(Func<ValueTask> onDispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => onDispose();
    }
}
