using System.Threading.Channels;

namespace Api.Services;

/// <summary>
/// A typed wrapper around a domain event for SSE streaming.
/// Contains the event type name and the event data.
/// </summary>
public record ClientEvent(string EventType, object Data);

/// <summary>
/// Singleton service that broadcasts domain events to all connected SSE subscribers.
/// Replaces SignalR's IHubContext for real-time event distribution.
/// Each connected client gets its own Channel&lt;ClientEvent&gt;.
/// </summary>
public class ClientEventBroadcaster
{
    private readonly List<Channel<ClientEvent>> _subscribers = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Creates a new subscription channel for a client.
    /// Returns an IAsyncEnumerable that yields events as they are broadcast.
    /// The channel is automatically removed when the enumeration completes.
    /// </summary>
    public async IAsyncEnumerable<ClientEvent> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<ClientEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Broadcasts an event to all connected subscribers.
    /// Non-blocking: if a subscriber's channel is full, the oldest event is dropped.
    /// </summary>
    public void Broadcast(ClientEvent evt)
    {
        lock (_lock)
        {
            foreach (var channel in _subscribers)
            {
                channel.Writer.TryWrite(evt);
            }
        }
    }
}
