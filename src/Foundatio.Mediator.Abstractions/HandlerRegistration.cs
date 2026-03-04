using System.ComponentModel;

namespace Foundatio.Mediator;

/// <summary>
/// Registration information for a handler
/// </summary>
public sealed class HandlerRegistration
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
    /// <param name="orderBefore">Fully qualified type names of handlers that this handler must execute before.</param>
    /// <param name="orderAfter">Fully qualified type names of handlers that this handler must execute after.</param>
    /// <param name="sourceHandlerName">The short name of the original handler class (e.g., "OrderHandler"). Used for diagnostic logging.</param>
    /// <param name="methodName">The handler method name (e.g., "HandleAsync"). Used for diagnostic logging.</param>
    /// <param name="returnTypeName">The display name of the handler return type (e.g., "Result&lt;Order&gt;"). Used for diagnostic logging.</param>
    public HandlerRegistration(string messageTypeName, string handlerClassName, HandleAsyncDelegate handleAsync, HandleDelegate? handle, bool isAsync, int order = int.MaxValue, PublishAsyncDelegate? publishAsync = null, string[]? orderBefore = null, string[]? orderAfter = null, string? sourceHandlerName = null, string? methodName = null, string? returnTypeName = null)
    {
        MessageTypeName = messageTypeName;
        HandlerClassName = handlerClassName;
        HandleAsync = handleAsync;
        Handle = handle;
        IsAsync = isAsync;
        Order = order;
        OrderBefore = orderBefore ?? [];
        OrderAfter = orderAfter ?? [];
        SourceHandlerName = sourceHandlerName;
        MethodName = methodName;
        ReturnTypeName = returnTypeName;
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

    /// <summary>
    /// Fully qualified type names of handlers that this handler must execute before during PublishAsync.
    /// </summary>
    public IReadOnlyList<string> OrderBefore { get; }

    /// <summary>
    /// Fully qualified type names of handlers that this handler must execute after during PublishAsync.
    /// </summary>
    public IReadOnlyList<string> OrderAfter { get; }

    /// <summary>
    /// The short name of the original handler class (e.g., "OrderHandler"). Used for diagnostic logging.
    /// </summary>
    public string? SourceHandlerName { get; }

    /// <summary>
    /// The handler method name (e.g., "HandleAsync"). Used for diagnostic logging.
    /// </summary>
    public string? MethodName { get; }

    /// <summary>
    /// The display name of the handler return type (e.g., "Result&lt;Order&gt;"). Used for diagnostic logging.
    /// </summary>
    public string? ReturnTypeName { get; }
}

/// <summary>
/// Delegate type for asynchronous handler dispatch. Used by source-generated handler wrappers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask<object?> HandleAsyncDelegate(IMediator mediator, object message, CancellationToken cancellationToken, Type? returnType);

/// <summary>
/// Delegate type for synchronous handler dispatch. Used by source-generated handler wrappers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate object? HandleDelegate(IMediator mediator, object message, CancellationToken cancellationToken, Type? returnType);
