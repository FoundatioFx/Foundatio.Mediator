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
/// Publisher that invokes all handlers concurrently and waits for all to complete.
/// </summary>
public class TaskWhenAllPublisher : INotificationPublisher
{
    public async ValueTask PublishAsync(IMediator mediator, IEnumerable<PublishAsyncDelegate> handlers, object message, CancellationToken cancellationToken)
    {
        var tasks = handlers.Select(h => h(mediator, message, cancellationToken));
        await Task.WhenAll(tasks.Select(t => t.AsTask()));
    }
}

/// <summary>
/// Publisher that fires all handlers in parallel without waiting for completion.
/// Use with caution - exceptions will not propagate and handlers may outlive the request.
/// </summary>
public class FireAndForgetPublisher : INotificationPublisher
{
    public ValueTask PublishAsync(IMediator mediator, IEnumerable<PublishAsyncDelegate> handlers, object message, CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
        {
            // Fire and forget - don't await, use Task.Run to ensure truly async execution
            _ = Task.Run(async () =>
            {
                try
                {
                    await handler(mediator, message, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Swallow exceptions - fire and forget semantics
                }
            }, CancellationToken.None);
        }
        return default;
    }
}
