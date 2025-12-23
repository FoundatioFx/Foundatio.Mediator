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
    ValueTask PublishAsync(IMediator mediator, PublishAsyncDelegate[] handlers, object message, CancellationToken cancellationToken);
}

/// <summary>
/// Publisher that invokes each handler sequentially.
/// All handlers execute even if one throws; an AggregateException with all exceptions is thrown after all complete.
/// </summary>
public class ForeachAwaitPublisher : INotificationPublisher
{
    public ValueTask PublishAsync(IMediator mediator, PublishAsyncDelegate[] handlers, object message, CancellationToken cancellationToken)
    {
        if (handlers.Length == 0)
            return default;

        // Sequential execution - start each handler after the previous completes
        // Loop until we find one that doesn't complete synchronously
        for (int i = 0; i < handlers.Length; i++)
        {
            var task = handlers[i](mediator, message, cancellationToken);
            if (!task.IsCompletedSuccessfully)
            {
                return AwaitRemainingAsync(task, mediator, handlers, message, cancellationToken, i + 1);
            }
        }

        return default;
    }

    private static async ValueTask AwaitRemainingAsync(ValueTask current, IMediator mediator, PublishAsyncDelegate[] handlers, object message, CancellationToken cancellationToken, int startIndex)
    {
        List<Exception>? exceptions = null;

        try
        {
            await current.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exceptions ??= [];
            exceptions.Add(ex);
        }

        for (int i = startIndex; i < handlers.Length; i++)
        {
            try
            {
                await handlers[i](mediator, message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        if (exceptions != null)
            throw new AggregateException(exceptions);
    }
}

/// <summary>
/// Publisher that invokes all handlers concurrently and waits for all to complete.
/// All handlers execute even if one throws; an AggregateException with all exceptions is thrown after all complete.
/// </summary>
public class TaskWhenAllPublisher : INotificationPublisher
{
    public ValueTask PublishAsync(IMediator mediator, PublishAsyncDelegate[] handlers, object message, CancellationToken cancellationToken)
    {
        if (handlers.Length == 0)
            return default;
        if (handlers.Length == 1)
            return handlers[0](mediator, message, cancellationToken);

        // Start all handlers concurrently
        var tasks = new ValueTask[handlers.Length];
        for (int i = 0; i < handlers.Length; i++)
            tasks[i] = handlers[i](mediator, message, cancellationToken);

        // Check if all completed synchronously and successfully
        for (int i = 0; i < tasks.Length; i++)
        {
            if (!tasks[i].IsCompletedSuccessfully)
                return AwaitAllAsync(tasks);
        }

        return default;
    }

    private static async ValueTask AwaitAllAsync(ValueTask[] tasks)
    {
        List<Exception>? exceptions = null;

        for (int i = 0; i < tasks.Length; i++)
        {
            try
            {
                await tasks[i].ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        if (exceptions != null)
            throw new AggregateException(exceptions);
    }
}

/// <summary>
/// Publisher that fires all handlers in parallel without waiting for completion.
/// Use with caution - exceptions will not propagate and handlers may outlive the request.
/// </summary>
public class FireAndForgetPublisher : INotificationPublisher
{
    public ValueTask PublishAsync(IMediator mediator, PublishAsyncDelegate[] handlers, object message, CancellationToken cancellationToken)
    {
        for (int i = 0; i < handlers.Length; i++)
        {
            var handler = handlers[i];
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
