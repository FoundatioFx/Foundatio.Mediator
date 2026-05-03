namespace Foundatio.Mediator;

/// <summary>
/// Configures the Foundatio.Mediator source generator for this assembly. Must be declared in each
/// project that contains handlers or middleware — settings do not cascade across project references.
/// Controls handler discovery, dependency injection lifetimes, endpoint generation, and other behavior.
/// </summary>
/// <example>
/// <code>
/// [assembly: MediatorConfiguration(
///     HandlerLifetime = MediatorLifetime.Scoped,
///     EndpointDiscovery = EndpointDiscovery.All
/// )]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MediatorConfigurationAttribute : Attribute
{
    /// <summary>
    /// Disables compile-time interceptors. When <c>true</c>, handler dispatch uses DI-based
    /// lookup at runtime instead of source-generated interception.
    /// Default: <c>false</c>.
    /// </summary>
    public bool DisableInterceptors { get; set; }

    /// <summary>
    /// Default DI lifetime for handlers. Individual handlers can override this
    /// via <c>[Handler(Lifetime = ...)]</c>.
    /// Default: <see cref="MediatorLifetime.Default"/> (internally cached, not registered in DI).
    /// </summary>
    public MediatorLifetime HandlerLifetime { get; set; }

    /// <summary>
    /// Default DI lifetime for middleware. Individual middleware can override this
    /// via <c>[Middleware(Lifetime = ...)]</c>.
    /// Default: <see cref="MediatorLifetime.Default"/> (internally cached, not registered in DI).
    /// </summary>
    public MediatorLifetime MiddlewareLifetime { get; set; }

    /// <summary>
    /// Disables OpenTelemetry tracing in generated handler code.
    /// Default: <c>false</c>.
    /// </summary>
    public bool DisableOpenTelemetry { get; set; }

    /// <summary>
    /// Disables automatic authorization checks in generated handler code and prevents
    /// registration of authorization services (<see cref="IHandlerAuthorizationService"/>,
    /// <see cref="IAuthorizationContextProvider"/>, and <c>IHttpContextAccessor</c>).
    /// When <c>true</c>, <c>[HandlerAuthorize]</c> attributes are ignored for inline mediator
    /// call auth checks. Endpoint-level <c>.RequireAuthorization()</c> is not affected.
    /// Default: <c>false</c>.
    /// </summary>
    public bool DisableAuthorization { get; set; }

    /// <summary>
    /// Controls how handlers are discovered. <see cref="HandlerDiscovery.All"/> finds handlers
    /// by naming convention (<c>*Handler</c>, <c>*Consumer</c>) and by explicit attributes/interfaces.
    /// <see cref="HandlerDiscovery.Explicit"/> requires <c>[Handler]</c> or <c>IHandler</c>.
    /// Default: <see cref="HandlerDiscovery.All"/>.
    /// </summary>
    public HandlerDiscovery HandlerDiscovery { get; set; }

    /// <summary>
    /// Excludes handlers in matching namespaces from source generation and discovery.
    /// Patterns support exact namespace matches (e.g., <c>"MyCompany.Messaging"</c>) and
    /// namespace prefix matches with a trailing wildcard (e.g., <c>"MyCompany.Messaging.*"</c>).
    /// Default: <c>null</c> (no namespace exclusions).
    /// </summary>
    public string[]? HandlerExcludeNamespacePatterns { get; set; }

    /// <summary>
    /// Strategy for publishing notifications to multiple handlers.
    /// <see cref="NotificationPublishStrategy.ForeachAwait"/> awaits each handler sequentially,
    /// <see cref="NotificationPublishStrategy.TaskWhenAll"/> runs them concurrently,
    /// and <see cref="NotificationPublishStrategy.FireAndForget"/> does not await.
    /// Default: <see cref="NotificationPublishStrategy.ForeachAwait"/>.
    /// </summary>
    public NotificationPublishStrategy NotificationPublishStrategy { get; set; }



    /// <summary>
    /// Adds a generation counter timestamp comment to generated files. Useful for verifying
    /// that incremental generation is working correctly during development.
    /// Default: <c>false</c>.
    /// </summary>
    public bool EnableGenerationCounter { get; set; }

    /// <summary>
    /// Controls which handlers have minimal API endpoints generated.
    /// <see cref="EndpointDiscovery.None"/> generates no endpoints,
    /// <see cref="EndpointDiscovery.Explicit"/> only generates endpoints for handlers
    /// marked with <see cref="HandlerEndpointAttribute"/>, and
    /// <see cref="EndpointDiscovery.All"/> generates endpoints for all discovered handlers.
    /// Default: <see cref="EndpointDiscovery.All"/>.
    /// </summary>
    public EndpointDiscovery EndpointDiscovery { get; set; }

    /// <summary>
    /// Global route prefix prepended to all generated endpoints. Group-level prefixes
    /// from <see cref="HandlerEndpointAttribute"/> are appended after this value
    /// (e.g. <c>"api"</c> + <c>"products"</c> → <c>"/api/products/..."</c>).
    /// Default: <c>"api"</c>.
    /// </summary>
    public string? EndpointRoutePrefix { get; set; } = "api";

    /// <summary>
    /// Endpoint filter types applied globally to all generated endpoints.
    /// Each type must implement <c>IEndpointFilter</c>. These run before any
    /// group-level or endpoint-level filters.
    /// Default: <c>null</c> (no filters).
    /// </summary>
    public Type[]? EndpointFilters { get; set; }

    /// <summary>
    /// Requires authorization on all handlers and generated endpoints. Individual handlers
    /// can opt out with <c>[HandlerAllowAnonymous]</c> or <c>[AllowAnonymous]</c> on the handler or method.
    /// When true, both generated endpoint auth (<c>.RequireAuthorization()</c>) and direct mediator
    /// call auth checks are enabled for all handlers in the assembly.
    /// Default: <c>false</c>.
    /// </summary>
    public bool AuthorizationRequired { get; set; }

    /// <summary>
    /// Authorization policy names applied to all handlers and generated endpoints.
    /// Can be overridden per-handler via <c>[HandlerAuthorize(Policies = ["..."])]</c>.
    /// Default: <c>null</c> (no policies).
    /// </summary>
    public string[]? AuthorizationPolicies { get; set; }

    /// <summary>
    /// Role names required for all handlers and generated endpoints.
    /// Can be overridden per-handler via <c>[HandlerAuthorize(Roles = ...)]</c>.
    /// Default: <c>null</c> (no role requirement).
    /// </summary>
    public string[]? AuthorizationRoles { get; set; }

    /// <summary>
    /// Controls how the endpoint summary is generated from the message type name.
    /// <see cref="EndpointSummaryStyle.Exact"/> uses the exact message type name (e.g., "GetProduct"),
    /// <see cref="EndpointSummaryStyle.Spaced"/> splits PascalCase into words (e.g., "Get Product").
    /// Default: <see cref="EndpointSummaryStyle.Exact"/>.
    /// </summary>
    public EndpointSummaryStyle EndpointSummaryStyle { get; set; }
}
