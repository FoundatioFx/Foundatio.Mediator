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

    /// <inheritdoc />
    public Task PublishAsync(string topic, ReadOnlyMemory<byte> body, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.TryGetValue(topic, out var entries))
            return Task.CompletedTask;

        var message = new PubSubMessage
        {
            Body = body,
            Headers = headers is IReadOnlyDictionary<string, string> ro
                ? ro
                : new Dictionary<string, string>(headers ?? new Dictionary<string, string>())
        };

        foreach (var entry in entries.Values)
            entry.Writer.TryWrite(message);

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
        var consumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in channel.Reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                {
                    await handler(msg, cts.Token).ConfigureAwait(false);
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
        foreach (var topicEntries in _subscriptions.Values)
        {
            foreach (var entry in topicEntries.Values)
                entry.Writer.TryComplete();
            topicEntries.Clear();
        }
        _subscriptions.Clear();
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
