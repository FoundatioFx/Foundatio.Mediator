using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

internal readonly record struct HandlerInfo
{
    public string Identifier { get; init; }
    public string FullName { get; init; }
    public string MethodName { get; init; }
    public TypeSymbolInfo MessageType { get; init; }
    public EquatableArray<string> MessageInterfaces { get; init; }
    public EquatableArray<string> MessageBaseClasses { get; init; }
    public bool HasReturnValue => !ReturnType.IsVoid;
    public TypeSymbolInfo ReturnType { get; init; }
    public bool IsAsync => ReturnType.IsTask || ReturnType.IsTuple || Middleware.Any(m => m.IsAsync) || Authorization.ShouldEnforce;
    public bool IsStatic { get; init; }
    public EquatableArray<ParameterInfo> Parameters { get; init; }
    public EquatableArray<CallSiteInfo> CallSites { get; init; }
    public EquatableArray<MiddlewareInfo> Middleware { get; init; }
    public EquatableArray<HandlerAttributeMetadataInfo> AttributeMetadata { get; init; }

    /// <summary>
    /// Authorization metadata for this handler, resolved from [HandlerAuthorize], [HandlerAllowAnonymous],
    /// [AllowAnonymous], and assembly-level defaults.
    /// </summary>
    public AuthorizationInfo Authorization { get; init; }

    /// <summary>
    /// Diagnostics related to ordering (e.g., circular dependency warnings).
    /// </summary>
    public EquatableArray<DiagnosticInfo> OrderingDiagnostics { get; init; }

    /// <summary>
    /// Middleware explicitly specified on this handler via [UseMiddleware] or custom attributes
    /// implementing IHandlerMiddlewareAttribute.
    /// </summary>
    public EquatableArray<HandlerMiddlewareReference> HandlerMiddlewareReferences { get; init; }

    public bool IsGenericHandlerClass { get; init; }
    public int GenericArity { get; init; }
    public EquatableArray<string> GenericTypeParameters { get; init; }
    public string? MessageGenericTypeDefinitionFullName { get; init; }
    public int MessageGenericArity { get; init; }
    public EquatableArray<string> GenericConstraints { get; init; }
    /// <summary>
    /// Whether this handler was discovered via explicit means (IHandler interface or [Handler] attribute)
    /// rather than conventional discovery (class name ending with Handler/Consumer).
    /// </summary>
    public bool IsExplicitlyDeclared { get; init; }

    /// <summary>
    /// The execution order for this handler during PublishAsync.
    /// Lower values execute first. Default is int.MaxValue.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Fully qualified type names of handlers that this handler must execute before during PublishAsync.
    /// </summary>
    public EquatableArray<string> OrderBefore { get; init; }

    /// <summary>
    /// Fully qualified type names of handlers that this handler must execute after during PublishAsync.
    /// </summary>
    public EquatableArray<string> OrderAfter { get; init; }

    /// <summary>
    /// The dependency injection lifetime for this handler.
    /// When null, uses the assembly-level MediatorConfiguration HandlerLifetime setting.
    /// </summary>
    public string? Lifetime { get; init; }

    /// <summary>
    /// Whether this handler class has constructor parameters requiring DI.
    /// </summary>
    public bool HasConstructorParameters { get; init; }

    /// <summary>
    /// Whether this handler accepts a batch of messages as its first parameter
    /// (<c>IReadOnlyList&lt;T&gt;</c> or <c>T[]</c>).
    /// </summary>
    public bool IsBatchHandler { get; init; }

    /// <summary>
    /// The inner item type for batch handlers (the <c>T</c> in <c>IReadOnlyList&lt;T&gt;</c> or <c>T[]</c>).
    /// Null for non-batch handlers.
    /// </summary>
    public TypeSymbolInfo? BatchItemType { get; init; }

    /// <summary>
    /// Endpoint metadata for generating minimal API endpoints.
    /// Null if endpoint generation is disabled for this handler.
    /// </summary>
    public EndpointInfo? Endpoint { get; init; }

    /// <summary>
    /// XML documentation summary extracted from the handler method.
    /// Used as the default endpoint summary for OpenAPI.
    /// </summary>
    public string? XmlDocSummary { get; init; }

    /// <summary>
    /// Whether the handler constructor requires dependency injection.
    /// True when the handler class has constructor parameters.
    /// </summary>
    public bool RequiresConstructorInjection => HasConstructorParameters;

    /// <summary>
    /// Whether the handler method requires dependency injection for parameters beyond message and CancellationToken.
    /// Parameters that don't require DI: the message parameter, CancellationToken.
    /// </summary>
    public bool RequiresMethodInjection =>
        Parameters.Any(p => !p.IsMessageParameter && !p.Type.IsCancellationToken);

    /// <summary>
    /// Whether this handler has no dependencies at all (no constructor or method injection required).
    /// When true for an instance handler, the handler can be cached in a static field.
    /// </summary>
    public bool HasNoDependencies =>
        !RequiresConstructorInjection &&
        !RequiresMethodInjection &&
        Middleware.All(m => m.HasNoDependencies);

    /// <summary>
    /// Whether this handler is static with no dependencies.
    /// When true, the handler can be called directly without any instance creation or DI resolution.
    /// This is the most optimal path - no async state machine, no middleware, no service provider needed.
    /// </summary>
    public bool IsStaticWithNoDependencies =>
        IsStatic &&
        !RequiresMethodInjection &&
        !Middleware.Any() &&
        !ReturnType.IsTuple;

    /// <summary>
    /// Whether this handler has any middleware attached.
    /// </summary>
    public bool HasMiddleware => Middleware.Any();

    /// <summary>
    /// Whether this handler has any before middleware methods.
    /// </summary>
    public bool HasBeforeMiddleware => Middleware.Any(m => m.BeforeMethod != null);

    /// <summary>
    /// Whether this handler has any after middleware methods.
    /// </summary>
    public bool HasAfterMiddleware => Middleware.Any(m => m.AfterMethod != null);

    /// <summary>
    /// Whether this handler has any finally middleware methods.
    /// These require try/catch/finally blocks to ensure they run even on exceptions.
    /// </summary>
    public bool HasFinallyMiddleware => Middleware.Any(m => m.FinallyMethod != null);

    /// <summary>
    /// Whether this handler has any execute middleware methods.
    /// Execute middleware wraps the entire pipeline (Before → Handler → After → Finally).
    /// </summary>
    public bool HasExecuteMiddleware => Middleware.Any(m => m.ExecuteMethod != null);

    /// <summary>
    /// Whether any middleware is async.
    /// </summary>
    public bool HasAsyncMiddleware => Middleware.Any(m => m.IsAsync);

    /// <summary>
    /// Whether this handler requires authorization checks in generated code.
    /// </summary>
    public bool RequiresAuthorization => Authorization.ShouldEnforce;

    /// <summary>
    /// Whether any middleware requires a HandlerExecutionInfo parameter, or authorization needs it.
    /// When true, the generated code must construct HandlerExecutionInfo.
    /// </summary>
    public bool RequiresHandlerExecutionInfo => Authorization.ShouldEnforce || Middleware.Any(m =>
        (m.BeforeMethod?.Parameters.Any(p => p.Type.IsHandlerExecutionInfo) ?? false) ||
        (m.AfterMethod?.Parameters.Any(p => p.Type.IsHandlerExecutionInfo) ?? false) ||
        (m.FinallyMethod?.Parameters.Any(p => p.Type.IsHandlerExecutionInfo) ?? false) ||
        (m.ExecuteMethod?.Parameters.Any(p => p.Type.IsHandlerExecutionInfo) ?? false));

    /// <summary>
    /// Whether any middleware requires instantiation (non-static middleware).
    /// </summary>
    public bool RequiresMiddlewareInstances => Middleware.Any(m => !m.IsStatic);

    /// <summary>
    /// Whether the handler returns a tuple with cascading messages.
    /// When true, non-first tuple items need to be published after handler execution.
    /// </summary>
    public bool HasCascadingMessages => ReturnType.IsTuple && ReturnType.TupleItems.Length > 1;

    /// <summary>
    /// Whether a service provider is needed for handler execution.
    /// True when handler or middleware requires DI resolution, or authorization checks are needed.
    /// </summary>
    public bool RequiresServiceProvider =>
        RequiresMethodInjection ||
        RequiresMiddlewareInstances ||
        RequiresAuthorization ||
        (!IsStatic && !HasNoDependencies);

    /// <summary>
    /// Whether a result variable is needed to store the handler's return value.
    /// True when handler has a return value and there's middleware, try/catch, or cascading.
    /// </summary>
    public bool RequiresResultVariable =>
        HasReturnValue && (HasMiddleware || HasFinallyMiddleware || HasCascadingMessages);

    /// <summary>
    /// Whether the generated code needs try/catch/finally blocks.
    /// True when there's finally middleware (must run even on exceptions).
    /// Note: OpenTelemetry also requires try/catch but that's checked at generation time with configuration.
    /// </summary>
    public bool RequiresTryCatch => HasFinallyMiddleware;

    /// <summary>
    /// Whether the handler can use a direct passthrough without async state machine.
    /// True for static handlers with no dependencies, middleware, cascading, or authorization.
    /// Note: OpenTelemetry disables this, but that's checked at generation time with configuration.
    /// </summary>
    public bool CanSkipAsyncStateMachine =>
        !RequiresAuthorization &&
        (IsStaticWithNoDependencies || (HasNoDependencies && !HasMiddleware && !HasCascadingMessages));

    /// <summary>
    /// Whether the handler must be resolved from DI on every invocation.
    /// True for Scoped or Transient lifetime handlers.
    /// </summary>
    public bool RequiresDIResolutionPerInvocation =>
        string.Equals(Lifetime, WellKnownTypes.LifetimeScoped, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Lifetime, WellKnownTypes.LifetimeTransient, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the handler has any explicit DI lifetime (Scoped, Transient, or Singleton).
    /// When true, the handler must be resolved from the DI container rather than cached by generated code.
    /// </summary>
    public bool HasExplicitLifetime =>
        RequiresDIResolutionPerInvocation ||
        string.Equals(Lifetime, WellKnownTypes.LifetimeSingleton, StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct ParameterInfo
{
    public string Name { get; init; }
    public TypeSymbolInfo Type { get; init; }
    public bool IsMessageParameter { get; init; }
}
