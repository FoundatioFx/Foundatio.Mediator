namespace Foundatio.Mediator;

/// <summary>
/// Registration information for a handler
/// </summary>
public class HandlerRegistration
{
    /// <summary>
    /// Creates a new handler registration
    /// </summary>
    /// <param name="messageTypeName">The fully qualified type name of the message</param>
    /// <param name="handlerClassName">The fully qualified type name of the generated handler wrapper class</param>
    /// <param name="handleAsync">The delegate to handle the message asynchronously</param>
    /// <param name="handle">The delegate to handle the message synchronously (null for async-only handlers)</param>
    /// <param name="isAsync">Whether the handler supports async operations</param>
    /// <param name="order">The execution order for this handler during PublishAsync. Lower values execute first.</param>
    /// <param name="publishAsync">The delegate for publish scenarios (discards return value, avoids allocation)</param>
    public HandlerRegistration(string messageTypeName, string handlerClassName, HandleAsyncDelegate handleAsync, HandleDelegate? handle, bool isAsync, int order = int.MaxValue, PublishAsyncDelegate? publishAsync = null)
    {
        MessageTypeName = messageTypeName;
        HandlerClassName = handlerClassName;
        HandleAsync = handleAsync;
        Handle = handle;
        IsAsync = isAsync;
        Order = order;
        // If no publish delegate provided, create a wrapper that discards the result
        PublishAsync = publishAsync ?? CreatePublishDelegate(handleAsync);
    }

    private static PublishAsyncDelegate CreatePublishDelegate(HandleAsyncDelegate handleAsync)
    {
        return (mediator, msg, cancellationToken) =>
        {
            var task = handleAsync(mediator, msg, cancellationToken, null);
            if (task.IsCompletedSuccessfully)
                return default;
            return AwaitAndDiscard(task);
        };

        static async ValueTask AwaitAndDiscard(ValueTask<object?> task)
        {
            await task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The fully qualified type name of the message this handler processes
    /// </summary>
    public string MessageTypeName { get; }

    /// <summary>
    /// The fully qualified type name of the generated handler wrapper class
    /// </summary>
    public string HandlerClassName { get; }

    /// <summary>
    /// The delegate to handle the message
    /// </summary>
    public HandleAsyncDelegate HandleAsync { get; }

    /// <summary>
    /// The delegate to handle the message synchronously (null for async-only handlers)
    /// </summary>
    public HandleDelegate? Handle { get; }

    /// <summary>
    /// The delegate for publish scenarios (discards return value, avoids allocation when sync)
    /// </summary>
    public PublishAsyncDelegate PublishAsync { get; }

    /// <summary>
    /// Whether the handler supports async operations
    /// </summary>
    public bool IsAsync { get; }

    /// <summary>
    /// The execution order for this handler during PublishAsync.
    /// Lower values execute first. Default is int.MaxValue.
    /// </summary>
    public int Order { get; }
}

public delegate ValueTask<object?> HandleAsyncDelegate(IMediator mediator, object message, CancellationToken cancellationToken, Type? returnType);
public delegate object? HandleDelegate(IMediator mediator, object message, CancellationToken cancellationToken, Type? returnType);
