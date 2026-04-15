using Microsoft.Extensions.Logging;

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
/// Delegate for publishing a batch of notification messages to a batch handler.
/// </summary>
/// <param name="mediator">The mediator instance.</param>
/// <param name="messages">The batch of notification messages.</param>
/// <param name="cancellationToken">The cancellation token.</param>
public delegate ValueTask PublishBatchAsyncDelegate(IMediator mediator, IReadOnlyList<object> messages, CancellationToken cancellationToken);

/// <summary>
/// Interface for publishing notifications.
/// </summary>
public interface INotificationPublisher
{
    ValueTask PublishAsync(IMediator mediator, PublishAsyncDelegate[] handlers, object message, CancellationToken cancellationToken);

    ValueTask PublishBatchAsync(IMediator mediator, PublishBatchAsyncDelegate[] batchHandlers, IReadOnlyList<object> messages, CancellationToken cancellationToken);
}

/// <summary>
/// Publisher that invokes each handler sequentially.
/// All handlers execute even if one throws; an AggregateException with all exceptions is thrown after all complete.
/// </summary>
public sealed class ForeachAwaitPublisher : INotificationPublisher
{
    public ValueTask PublishAsync(IMediator mediator, PublishAsyncDelegate[] handlers, object message, CancellationToken cancellationToken)
    {
        if (handlers.Length == 0)
            return default;

        // Sequential execution - start each handler after the previous completes
        // Loop until we find one that doesn't complete synchronously or throws
        for (int i = 0; i < handlers.Length; i++)
        {
            ValueTask task;
            try
            {
                task = handlers[i](mediator, message, cancellationToken);
            }
            catch (Exception ex)
            {
                // Handler threw synchronously before returning a ValueTask.
                // Continue executing remaining handlers and aggregate exceptions.
                return AwaitRemainingAfterSyncThrowAsync(ex, mediator, handlers, message, cancellationToken, i + 1);
            }

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

    private static async ValueTask AwaitRemainingAfterSyncThrowAsync(Exception syncException, IMediator mediator, PublishAsyncDelegate[] handlers, object message, CancellationToken cancellationToken, int startIndex)
    {
        List<Exception> exceptions = [syncException];

        for (int i = startIndex; i < handlers.Length; i++)
        {
            try
            {
                await handlers[i](mediator, message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        throw new AggregateException(exceptions);
    }

    public ValueTask PublishBatchAsync(IMediator mediator, PublishBatchAsyncDelegate[] batchHandlers, IReadOnlyList<object> messages, CancellationToken cancellationToken)
    {
        if (batchHandlers.Length == 0)
            return default;

        for (int i = 0; i < batchHandlers.Length; i++)
        {
            ValueTask task;
            try
            {
                task = batchHandlers[i](mediator, messages, cancellationToken);
            }
            catch (Exception ex)
            {
                return AwaitRemainingBatchAfterSyncThrowAsync(ex, mediator, batchHandlers, messages, cancellationToken, i + 1);
            }

            if (!task.IsCompletedSuccessfully)
            {
                return AwaitRemainingBatchAsync(task, mediator, batchHandlers, messages, cancellationToken, i + 1);
            }
        }

        return default;
    }

    private static async ValueTask AwaitRemainingBatchAsync(ValueTask current, IMediator mediator, PublishBatchAsyncDelegate[] handlers, IReadOnlyList<object> messages, CancellationToken cancellationToken, int startIndex)
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
                await handlers[i](mediator, messages, cancellationToken).ConfigureAwait(false);
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

    private static async ValueTask AwaitRemainingBatchAfterSyncThrowAsync(Exception syncException, IMediator mediator, PublishBatchAsyncDelegate[] handlers, IReadOnlyList<object> messages, CancellationToken cancellationToken, int startIndex)
    {
        List<Exception> exceptions = [syncException];

        for (int i = startIndex; i < handlers.Length; i++)
        {
            try
            {
                await handlers[i](mediator, messages, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        throw new AggregateException(exceptions);
    }
}

/// <summary>
/// Publisher that invokes all handlers concurrently and waits for all to complete.
/// All handlers execute even if one throws; an AggregateException with all exceptions is thrown after all complete.
/// </summary>
public sealed class TaskWhenAllPublisher : INotificationPublisher
{
    public ValueTask PublishAsync(IMediator mediator, PublishAsyncDelegate[] handlers, object message, CancellationToken cancellationToken)
    {
        if (handlers.Length == 0)
            return default;
        if (handlers.Length == 1)
            return handlers[0](mediator, message, cancellationToken);

        // Start all handlers concurrently
        // Wrap invocations in try/catch so a synchronous throw doesn't prevent remaining handlers from starting
        var tasks = new ValueTask[handlers.Length];
        List<Exception>? syncExceptions = null;
        for (int i = 0; i < handlers.Length; i++)
        {
            try
            {
                tasks[i] = handlers[i](mediator, message, cancellationToken);
            }
            catch (Exception ex)
            {
                syncExceptions ??= [];
                syncExceptions.Add(ex);
                tasks[i] = default; // Mark as completed (no-op)
            }
        }

        // If we had sync throws, must await all tasks and aggregate
        if (syncExceptions != null)
            return AwaitAllWithSyncExceptionsAsync(tasks, syncExceptions);

        // Check if all completed synchronously and successfully
        for (int i = 0; i < tasks.Length; i++)
        {
            if (!tasks[i].IsCompletedSuccessfully)
                return AwaitAllAsync(tasks);
        }

        return default;
    }

    public ValueTask PublishBatchAsync(IMediator mediator, PublishBatchAsyncDelegate[] batchHandlers, IReadOnlyList<object> messages, CancellationToken cancellationToken)
    {
        if (batchHandlers.Length == 0)
            return default;
        if (batchHandlers.Length == 1)
            return batchHandlers[0](mediator, messages, cancellationToken);

        var tasks = new ValueTask[batchHandlers.Length];
        List<Exception>? syncExceptions = null;
        for (int i = 0; i < batchHandlers.Length; i++)
        {
            try
            {
                tasks[i] = batchHandlers[i](mediator, messages, cancellationToken);
            }
            catch (Exception ex)
            {
                syncExceptions ??= [];
                syncExceptions.Add(ex);
                tasks[i] = default;
            }
        }

        if (syncExceptions != null)
            return AwaitAllWithSyncExceptionsAsync(tasks, syncExceptions);

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

    private static async ValueTask AwaitAllWithSyncExceptionsAsync(ValueTask[] tasks, List<Exception> exceptions)
    {
        for (int i = 0; i < tasks.Length; i++)
        {
            try
            {
                await tasks[i].ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        throw new AggregateException(exceptions);
    }
}

/// <summary>
/// Publisher that fires all handlers in parallel without waiting for completion.
/// Use with caution - exceptions will not propagate and handlers may outlive the request.
/// </summary>
public sealed class FireAndForgetPublisher : INotificationPublisher
{
    private readonly ILogger<FireAndForgetPublisher>? _logger;

    /// <summary>
    /// Creates a new <see cref="FireAndForgetPublisher"/> instance.
    /// </summary>
    /// <param name="logger">Optional logger for recording handler exceptions. When resolved from DI, this is injected automatically.</param>
    public FireAndForgetPublisher(ILogger<FireAndForgetPublisher>? logger = null)
    {
        _logger = logger;
    }

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
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Fire-and-forget handler failed for message type {MessageType}", message.GetType().Name);
                }
            }, CancellationToken.None);
        }

        return default;
    }

    public ValueTask PublishBatchAsync(IMediator mediator, PublishBatchAsyncDelegate[] batchHandlers, IReadOnlyList<object> messages, CancellationToken cancellationToken)
    {
        for (int i = 0; i < batchHandlers.Length; i++)
        {
            var batchHandler = batchHandlers[i];
            _ = Task.Run(async () =>
            {
                try
                {
                    await batchHandler(mediator, messages, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Fire-and-forget batch handler failed for {Count} message(s)", messages.Count);
                }
            }, CancellationToken.None);
        }

        return default;
    }
}
