namespace Foundatio.Mediator;

/// <summary>
/// Delegate for publishing a notification.
/// </summary>
/// <param name="mediator">The mediator instance.</param>
/// <param name="message">The notification message.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns></returns>
public delegate ValueTask PublishAsyncDelegate(IMediator mediator, object message, CancellationToken cancellationToken);

/// <summary>
/// Interface for publishing notifications.
/// </summary>
public interface INotificationPublisher
{
    ValueTask PublishAsync(IMediator mediator, IEnumerable<PublishAsyncDelegate> handlers, object message, CancellationToken cancellationToken);
}

/// <summary>
/// Publisher that invokes each handler sequentially.
/// </summary>
public class ForeachAwaitPublisher : INotificationPublisher
{
    public async ValueTask PublishAsync(IMediator mediator, IEnumerable<PublishAsyncDelegate> handlers, object message, CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
        {
            await handler(mediator, message, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Publisher that invokes all handlers concurrently.
/// </summary>
public class TaskWhenAllPublisher : INotificationPublisher
{
    public async ValueTask PublishAsync(IMediator mediator, IEnumerable<PublishAsyncDelegate> handlers, object message, CancellationToken cancellationToken)
    {
        var tasks = handlers.Select(h => h(mediator, message, cancellationToken));
        await Task.WhenAll(tasks.Select(t => t.AsTask()));
    }
}
