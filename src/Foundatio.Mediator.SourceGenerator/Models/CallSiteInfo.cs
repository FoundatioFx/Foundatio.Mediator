using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal readonly record struct CallSiteInfo
{
    public string MethodName { get; init; }
    public TypeSymbolInfo MessageType { get; init; }
    public TypeSymbolInfo? ResponseType { get; init; }
    public bool HasReturnValue => ResponseType is not null && ResponseType?.IsVoid == false;
    public bool IsAsync => ResponseType?.IsTask == true;
    public bool IsPublish { get; init; }
    public LocationInfo Location { get; init; }
}
