namespace Foundatio.Mediator.Models;

internal readonly record struct CallSiteInfo
{
    public string MethodName { get; init; }
    public TypeSymbolInfo MessageType { get; init; }
    public TypeSymbolInfo ResponseType { get; init; }
    public bool HasReturnValue => !ResponseType.IsVoid;
    public bool IsAsync => ResponseType.IsTask;
    public bool IsPublish { get; init; }
    public LocationInfo Location { get; init; }
    /// <summary>
    /// Whether this call site uses the IRequest&lt;TResponse&gt; overload instead of the object overload.
    /// </summary>
    public bool UsesIRequestOverload { get; init; }
}
