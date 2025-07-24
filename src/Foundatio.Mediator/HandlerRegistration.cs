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
    public HandlerRegistration(string messageTypeName, Func<IMediator, object, CancellationToken, Type?, ValueTask<object?>> handleAsync, Func<IMediator, object, CancellationToken, Type?, object?>? handle, bool isAsync)
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
    public Func<IMediator, object, CancellationToken, Type?, ValueTask<object?>> HandleAsync { get; }

    /// <summary>
    /// The delegate to handle the message synchronously (null for async-only handlers)
    /// </summary>
    public Func<IMediator, object, CancellationToken, Type?, object?>? Handle { get; }

    /// <summary>
    /// Whether the handler supports async operations
    /// </summary>
    public bool IsAsync { get; }
}
