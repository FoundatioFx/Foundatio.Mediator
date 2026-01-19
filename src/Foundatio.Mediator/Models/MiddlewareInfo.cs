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
    /// Whether the middleware constructor requires dependency injection.
    /// </summary>
    public bool RequiresConstructorInjection => HasConstructorParameters;

    /// <summary>
    /// Whether any middleware method requires dependency injection for parameters.
    /// </summary>
    public bool RequiresMethodInjection => HasMethodDIParameters;

    /// <summary>
    /// Whether this middleware has no dependencies (no constructor or method injection required).
    /// True when the middleware is static or has no constructor parameters and no method DI parameters.
    /// </summary>
    public bool HasNoDependencies => IsStatic || (!RequiresConstructorInjection && !RequiresMethodInjection);

    #region Middleware Instantiation Strategy

    /// <summary>
    /// Whether middleware instances can be cached (singleton-like behavior).
    /// True when middleware has no dependencies or explicit Singleton lifetime.
    /// </summary>
    public bool CanCacheInstance =>
        HasNoDependencies ||
        string.Equals(Lifetime, "Singleton", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the middleware must be resolved from DI on every invocation.
    /// True for Scoped or Transient lifetime middleware.
    /// </summary>
    public bool RequiresDIResolutionPerInvocation =>
        string.Equals(Lifetime, "Scoped", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Lifetime, "Transient", StringComparison.OrdinalIgnoreCase);

    #endregion
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
