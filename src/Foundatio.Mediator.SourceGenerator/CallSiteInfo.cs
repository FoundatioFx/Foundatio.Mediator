using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal readonly record struct CallSiteInfo
{
    public string MethodName { get; init; }
    public TypeSymbolInfo MessageType { get; init; }
    public TypeSymbolInfo ResponseType { get; init; }
    public bool IsAsync { get; init; }
    public bool IsPublish { get; init; }
    public LocationInfo Location { get; init; }
}
