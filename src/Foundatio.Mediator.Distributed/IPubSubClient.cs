namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Transport-agnostic pub/sub abstraction used by the distributed notification system.
/// Implementations fan messages out to all subscribers (topic-based publish/subscribe).
/// </summary>
public interface IPubSubClient : IAsyncDisposable
{
    /// <summary>
    /// Publishes a message to all subscribers of the specified topic.
    /// </summary>
    /// <param name="topic">The topic to publish to.</param>
    /// <param name="message">The outbound message containing body and optional headers.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task PublishAsync(string topic, PubSubEntry message, CancellationToken cancellationToken = default);

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
    Task EnsureTopicsAsync(IReadOnlyList<string> topics, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    ValueTask IAsyncDisposable.DisposeAsync() => default;
}
