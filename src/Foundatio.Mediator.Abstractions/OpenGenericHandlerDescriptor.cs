namespace Foundatio.Mediator;

/// <summary>
/// Descriptor for an open generic handler method. Used to construct concrete <see cref="HandlerRegistration"/> instances at runtime
/// when a closed generic message type is invoked or published.
/// </summary>
public sealed class OpenGenericHandlerDescriptor
{
    public OpenGenericHandlerDescriptor(Type messageTypeGenericDefinition, Type wrapperGenericTypeDefinition, bool isAsync)
    {
        MessageTypeGenericDefinition = messageTypeGenericDefinition ?? throw new ArgumentNullException(nameof(messageTypeGenericDefinition));
        WrapperGenericTypeDefinition = wrapperGenericTypeDefinition ?? throw new ArgumentNullException(nameof(wrapperGenericTypeDefinition));
        IsAsync = isAsync;
    }

    /// <summary>
    /// The open generic (generic type definition) of the message type (e.g. typeof(UpdateEntity&lt;&gt;)).
    /// </summary>
    public Type MessageTypeGenericDefinition { get; }

    /// <summary>
    /// The open generic (generic type definition) of the generated handler wrapper static class.
    /// </summary>
    public Type WrapperGenericTypeDefinition { get; }

    /// <summary>
    /// Indicates if the handler method should be treated as async.
    /// </summary>
    public bool IsAsync { get; }
}
