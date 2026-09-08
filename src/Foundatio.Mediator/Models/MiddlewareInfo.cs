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
    public MiddlewareMethodInfo? ExecuteMethod { get; init; }
    public bool IsStatic { get; init; }
    public bool IsAsync => BeforeMethod?.IsAsync == true || AfterMethod?.IsAsync == true || FinallyMethod?.IsAsync == true || ExecuteMethod?.IsAsync == true;
    public int? Order { get; init; }

    /// <summary>
    /// Fully qualified type names of middleware that this middleware must run before.
    /// </summary>
    public EquatableArray<string> OrderBefore { get; init; }

    /// <summary>
    /// Fully qualified type names of middleware that this middleware must run after.
    /// </summary>
    public EquatableArray<string> OrderAfter { get; init; }

    public Accessibility DeclaredAccessibility { get; init; }
    public string AssemblyName { get; init; }
    public EquatableArray<DiagnosticInfo> Diagnostics { get; init; }

    /// <summary>
    /// Whether this middleware was explicitly declared via [Middleware] attribute (not just by naming convention).
    /// </summary>
    public bool IsExplicitlyDeclared { get; init; }

    /// <summary>
    /// Whether this middleware should only be applied when explicitly referenced via [UseMiddleware].
    /// When true, the middleware is not automatically applied based on message type matching.
    /// </summary>
    public bool ExplicitOnly { get; init; }

    /// <summary>
    /// The DI lifetime for this middleware when explicitly set via [Middleware(Lifetime = ...)] attribute.
    /// Null means use the assembly-level MediatorConfiguration MiddlewareLifetime setting.
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
        string.Equals(Lifetime, WellKnownTypes.LifetimeSingleton, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the middleware must be resolved from DI on every invocation.
    /// True for Scoped or Transient lifetime middleware.
    /// </summary>
    public bool RequiresDIResolutionPerInvocation =>
        string.Equals(Lifetime, WellKnownTypes.LifetimeScoped, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Lifetime, WellKnownTypes.LifetimeTransient, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the middleware has any explicit DI lifetime (Scoped, Transient, or Singleton).
    /// When true, the middleware must be resolved from the DI container rather than cached by generated code.
    /// </summary>
    public bool HasExplicitLifetime =>
        RequiresDIResolutionPerInvocation ||
        string.Equals(Lifetime, WellKnownTypes.LifetimeSingleton, StringComparison.OrdinalIgnoreCase);

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
