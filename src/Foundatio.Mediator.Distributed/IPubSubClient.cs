namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Transport-agnostic pub/sub abstraction used by the distributed notification system.
/// Implementations fan messages out to all subscribers (topic-based publish/subscribe).
/// </summary>
/// <remarks>
/// <para>
/// Pub/sub is <b>fire-and-forget</b>: there is no acknowledgment, retry, or dead-letter
/// mechanism. If a subscriber's handler throws, the message is considered delivered and
/// will not be redelivered. Implementations should delete/acknowledge the transport
/// message after invoking the handler regardless of success or failure.
/// </para>
/// <para>
/// For at-least-once delivery with retries and dead-lettering, use <see cref="IQueueClient"/> instead.
/// </para>
/// </remarks>
public interface IPubSubClient : IAsyncDisposable
{
    /// <summary>
    /// Publishes one or more messages to all subscribers of the specified topic.
    /// Implementations may use transport-native batch APIs for better throughput.
    /// </summary>
    /// <param name="topic">The topic to publish to.</param>
    /// <param name="messages">The outbound messages containing body and optional headers.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task PublishAsync(string topic, IReadOnlyList<PubSubEntry> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to a topic. The returned <see cref="IAsyncDisposable"/> unsubscribes when disposed.
    /// </summary>
    /// <param name="topic">The topic to subscribe to.</param>
    /// <param name="handler">Callback invoked for each received message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A handle that unsubscribes when disposed.</returns>
    Task<IAsyncDisposable> SubscribeAsync(string topic, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the specified topics and per-node subscription infrastructure exist.
    /// Implementations create topics, per-node queues, and subscriptions so that
    /// <see cref="SubscribeAsync"/> can skip to polling without additional API calls.
    /// </summary>
    Task EnsureTopicsAsync(IReadOnlyList<TopicDefinition> topics, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    ValueTask IAsyncDisposable.DisposeAsync() => default;
}
