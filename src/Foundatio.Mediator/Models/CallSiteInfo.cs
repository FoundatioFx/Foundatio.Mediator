using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

internal readonly record struct CallSiteInfo
{
    public string MethodName { get; init; }
    public TypeSymbolInfo MessageType { get; init; }
    public TypeSymbolInfo ResponseType { get; init; }
    public bool HasReturnValue => !ResponseType.IsVoid;
    /// <summary>
    /// Whether the call site method is async (InvokeAsync/PublishAsync vs Invoke).
    /// </summary>
    public bool IsAsyncMethod { get; init; }
    public bool IsPublish { get; init; }
    public LocationInfo Location { get; init; }
    /// <summary>
    /// Whether this call site uses the IRequest&lt;TResponse&gt; overload instead of the object overload.
    /// </summary>
    public bool UsesIRequestOverload { get; init; }
    /// <summary>
    /// Full names of interfaces implemented by the message type.
    /// Used for finding handlers that handle interface types during PublishAsync.
    /// </summary>
    public EquatableArray<string> MessageInterfaces { get; init; }
    /// <summary>
    /// Full names of base classes of the message type.
    /// Used for finding handlers that handle base class types during PublishAsync.
    /// </summary>
    public EquatableArray<string> MessageBaseClasses { get; init; }
}
