using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Models;

internal readonly record struct MiddlewareInfo
{
    public string Identifier { get; init; }
    public string FullName { get; init; }
    public TypeSymbolInfo MessageType { get; init; }
    public MiddlewareMethodInfo? BeforeMethod { get; init; }
    public MiddlewareMethodInfo? AfterMethod { get; init; }
    public MiddlewareMethodInfo? FinallyMethod { get; init; }
    public bool IsStatic { get; init; }
    public bool IsAsync => BeforeMethod?.IsAsync == true || AfterMethod?.IsAsync == true || FinallyMethod?.IsAsync == true;
    public int? Order { get; init; }
    public Accessibility DeclaredAccessibility { get; init; }
    public string AssemblyName { get; init; }
    public EquatableArray<DiagnosticInfo> Diagnostics { get; init; }
}

internal readonly record struct MiddlewareMethodInfo
{
    public string MethodName { get; init; }
    public bool IsAsync => ReturnType.IsTask;
    public bool HasReturnValue => !ReturnType.IsVoid;
    public bool IsStatic { get; init; }
    public TypeSymbolInfo MessageType { get; init; }
    public TypeSymbolInfo ReturnType { get; init; }
    public EquatableArray<ParameterInfo> Parameters { get; init; }
}
