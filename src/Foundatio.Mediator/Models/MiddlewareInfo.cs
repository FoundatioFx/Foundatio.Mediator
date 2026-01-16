using Foundatio.Mediator.Utility;

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

    /// <summary>
    /// Whether this middleware was explicitly declared via [Middleware] attribute (not just by naming convention).
    /// </summary>
    public bool IsExplicitlyDeclared { get; init; }

    /// <summary>
    /// The DI lifetime for this middleware when explicitly set via [Middleware(Lifetime = ...)] attribute.
    /// Null means use the project-level MediatorDefaultMiddlewareLifetime setting.
    /// </summary>
    public string? Lifetime { get; init; }

    /// <summary>
    /// Whether this middleware class has constructor parameters (indicating DI dependencies).
    /// </summary>
    public bool HasConstructorParameters { get; init; }

    /// <summary>
    /// Whether any middleware method has DI parameters beyond message, HandlerExecutionInfo, exception, or before method return values.
    /// </summary>
    public bool HasMethodDIParameters { get; init; }

    /// <summary>
    /// Whether this middleware can use a fast path (no DI required).
    /// True when the middleware is static or has no constructor parameters and no method DI parameters.
    /// </summary>
    public bool CanUseFastPath => IsStatic || (!HasConstructorParameters && !HasMethodDIParameters);
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
