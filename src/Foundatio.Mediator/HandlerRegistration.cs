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
    /// <param name="handleAsync">The delegate to handle the message asynchronously</param>
    /// <param name="handle">The delegate to handle the message synchronously (null for async-only handlers)</param>
    /// <param name="isAsync">Whether the handler supports async operations</param>
    public HandlerRegistration(string messageTypeName, HandleAsyncDelegate handleAsync, HandleDelegate? handle, bool isAsync)
    {
        MessageTypeName = messageTypeName;
        HandleAsync = handleAsync;
        Handle = handle;
        IsAsync = isAsync;
    }

    /// <summary>
    /// The fully qualified type name of the message this handler processes
    /// </summary>
    public string MessageTypeName { get; }

    /// <summary>
    /// The delegate to handle the message
    /// </summary>
    public HandleAsyncDelegate HandleAsync { get; }

    /// <summary>
    /// The delegate to handle the message synchronously (null for async-only handlers)
    /// </summary>
    public HandleDelegate? Handle { get; }

    /// <summary>
    /// Whether the handler supports async operations
    /// </summary>
    public bool IsAsync { get; }
}

public delegate ValueTask<object?> HandleAsyncDelegate(IMediator mediator, object message, CancellationToken cancellationToken, Type? returnType);
public delegate object? HandleDelegate(IMediator mediator, object message, CancellationToken cancellationToken, Type? returnType);
