using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal readonly record struct HandlerInfo
{
    public string HandlerTypeName { get; init; }
    public string MethodName { get; init; }
    public TypeSymbolInfo MessageType { get; init; }
    public TypeSymbolInfo ReturnType { get; init; }
    public bool IsAsync => ReturnType.IsTask || Middleware.Any(m => m.IsAsync);
    public bool IsStatic { get; init; }
    public EquatableArray<ParameterInfo> Parameters { get; init; }
    public EquatableArray<CallSiteInfo> CallSites { get; init; }
    public EquatableArray<MiddlewareInfo> Middleware { get; init; }
}

internal readonly record struct ParameterInfo
{
    public string Name { get; init; }
    public TypeSymbolInfo Type { get; init; }
    public bool IsMessageParameter { get; init; }
}
