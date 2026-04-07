using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

/// <summary>
/// Contains metadata for generating a minimal API endpoint from a handler.
/// </summary>
internal readonly record struct EndpointInfo
{
    /// <summary>
    /// The HTTP method (GET, POST, PUT, DELETE, PATCH).
    /// </summary>
    public string HttpMethod { get; init; }

    /// <summary>
    /// The route template (e.g., "/api/products/{productId}").
    /// </summary>
    public string Route { get; init; }

    /// <summary>
    /// Whether the route was explicitly set via attribute (vs auto-generated).
    /// </summary>
    public bool HasExplicitRoute { get; init; }

    /// <summary>
    /// The endpoint operation name for OpenAPI (operationId).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The endpoint summary for OpenAPI.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// The endpoint description for OpenAPI.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The group name for API grouping (from HandlerEndpointGroupAttribute).
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// OpenAPI tags for the group. When non-empty, used instead of <see cref="Group"/> for <c>.WithTags()</c>.
    /// </summary>
    public EquatableArray<string> GroupTags { get; init; }

    /// <summary>
    /// The route prefix from the group (e.g., "/api/products").
    /// </summary>
    public string? GroupRoutePrefix { get; init; }

    /// <summary>
    /// Route parameters extracted from the message type properties.
    /// </summary>
    public EquatableArray<EndpointParameterInfo> RouteParameters { get; init; }

    /// <summary>
    /// Query parameters for GET/DELETE requests.
    /// </summary>
    public EquatableArray<EndpointParameterInfo> QueryParameters { get; init; }

    /// <summary>
    /// Parameters with explicit binding attributes ([FromHeader], [FromQuery], [FromRoute])
    /// that must be extracted as separate endpoint lambda parameters and merged into the message.
    /// </summary>
    public EquatableArray<EndpointParameterInfo> BindingParameters { get; init; }

    /// <summary>
    /// Whether the message should be bound from body (POST/PUT/PATCH).
    /// </summary>
    public bool BindFromBody { get; init; }

    /// <summary>
    /// Whether the message type supports [AsParameters] binding.
    /// True when message has a parameterless constructor with settable properties.
    /// </summary>
    public bool SupportsAsParameters { get; init; }

    /// <summary>
    /// Whether the message type has a public parameterless constructor (or a record with all-default parameters).
    /// Used to validate GET/DELETE endpoint generation that may need to construct the message via <c>new T()</c>.
    /// </summary>
    public bool HasParameterlessConstructor { get; init; }

    /// <summary>
    /// Whether this handler should generate an endpoint.
    /// </summary>
    public bool GenerateEndpoint { get; init; }

    /// <summary>
    /// Reason this handler was excluded from endpoint generation, if applicable.
    /// Null when the handler is not excluded.
    /// </summary>
    public string? ExcludeReason { get; init; }

    /// <summary>
    /// Whether this handler has an explicit [HandlerEndpoint] attribute on the method or class.
    /// Used by "Explicit" discovery mode to distinguish explicitly marked handlers.
    /// </summary>
    public bool HasExplicitEndpointAttribute { get; init; }

    /// <summary>
    /// Whether this endpoint requires authentication.
    /// </summary>
    public bool RequireAuth { get; init; }

    /// <summary>
    /// Whether this endpoint allows anonymous access, overriding any group-level RequireAuthorization.
    /// Set when the handler method or class has [AllowAnonymous].
    /// </summary>
    public bool AllowAnonymous { get; init; }

    /// <summary>
    /// Required roles for this endpoint (any of).
    /// </summary>
    public EquatableArray<string> Roles { get; init; }

    /// <summary>
    /// Required authorization policies for this endpoint (all must be satisfied).
    /// </summary>
    public EquatableArray<string> Policies { get; init; }

    /// <summary>
    /// Endpoint filter type names (fully qualified) from [HandlerEndpoint] or class-level [HandlerEndpoint].
    /// </summary>
    public EquatableArray<string> Filters { get; init; }

    /// <summary>
    /// Group-level endpoint filter type names (fully qualified) from [HandlerEndpointGroup].
    /// Kept separate so the generator can emit them on the MapGroup rather than individual endpoints.
    /// </summary>
    public EquatableArray<string> GroupFilters { get; init; }

    /// <summary>
    /// The fully qualified type name of the inner value type for Produces metadata.
    /// For Result&lt;T&gt; returns, this is the fully qualified name of T.
    /// For non-Result non-void returns, this is the full return type name.
    /// Null for void handlers or non-generic Result.
    /// </summary>
    public string? ProducesType { get; init; }

    /// <summary>
    /// Additional HTTP status codes this endpoint can produce (e.g., 404, 422, 500).
    /// Used to emit <c>.ProducesProblem(statusCode)</c> for OpenAPI documentation.
    /// </summary>
    public EquatableArray<int> ProducesStatusCodes { get; init; }

    /// <summary>
    /// When true, the group route prefix bypasses the global <c>EndpointRoutePrefix</c>.
    /// Set when the group <c>RoutePrefix</c> starts with <c>/</c> (e.g., <c>"/health"</c>).
    /// </summary>
    public bool GroupBypassGlobalPrefix { get; init; }

    /// <summary>
    /// When true, the explicit route bypasses both the global and group prefixes.
    /// Set when the <c>[HandlerEndpoint(Route = "/...")]</c> starts with <c>/</c>.
    /// </summary>
    public bool RouteBypassPrefixes { get; init; }

    /// <summary>
    /// Whether this endpoint returns an <c>IAsyncEnumerable&lt;T&gt;</c> and should be treated as streaming.
    /// </summary>
    public bool IsStreaming { get; init; }

    /// <summary>
    /// The streaming format: "Default" (JSON array) or "ServerSentEvents" (SSE via TypedResults.ServerSentEvents).
    /// </summary>
    public string? StreamingFormat { get; init; }

    /// <summary>
    /// The fully qualified element type name for streaming handlers (<c>T</c> in <c>IAsyncEnumerable&lt;T&gt;</c>).
    /// </summary>
    public string? StreamingItemType { get; init; }

    /// <summary>
    /// The SSE event type name. When non-null, passed as the <c>eventType</c> parameter
    /// to <c>TypedResults.ServerSentEvents()</c>.
    /// </summary>
    public string? SseEventType { get; init; }

    /// <summary>
    /// Whether the handler body contains a <c>Result.Created()</c> call.
    /// Used to determine the success status code: 201 when true, 200 otherwise.
    /// </summary>
    public bool UsesResultCreated { get; init; }

    /// <summary>
    /// Explicit success status code from <c>[HandlerEndpoint(SuccessStatusCode = ...)]</c>.
    /// When 0, the status code is auto-detected from handler body analysis.
    /// </summary>
    public int ExplicitSuccessStatusCode { get; init; }

    /// <summary>
    /// The API versions this endpoint belongs to (e.g., ["1", "2"]).
    /// When empty, the endpoint is unversioned and generated without a version prefix.
    /// Resolved by cascading method-level → group-level attributes.
    /// </summary>
    public EquatableArray<string> ApiVersions { get; init; }

    /// <summary>
    /// Whether this endpoint is marked as deprecated.
    /// When true, the generated endpoint includes deprecation metadata for OpenAPI.
    /// </summary>
    public bool Deprecated { get; init; }
}

/// <summary>
/// Contains metadata for a route or query parameter.
/// </summary>
internal readonly record struct EndpointParameterInfo
{
    /// <summary>
    /// The parameter name as it appears in the route template (camelCase).
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The property name on the message type (PascalCase).
    /// </summary>
    public string PropertyName { get; init; }

    /// <summary>
    /// The parameter type information.
    /// </summary>
    public TypeSymbolInfo Type { get; init; }

    /// <summary>
    /// Whether this parameter is optional (has a default value).
    /// </summary>
    public bool IsOptional { get; init; }

    /// <summary>
    /// Whether this is a route parameter (vs query parameter).
    /// </summary>
    public bool IsRouteParameter { get; init; }

    /// <summary>
    /// The full attribute syntax to emit on the endpoint lambda parameter
    /// (e.g., <c>[Microsoft.AspNetCore.Mvc.FromHeader(Name = "X-Tenant-Id")]</c>).
    /// Null when the parameter uses default binding (route or query convention).
    /// </summary>
    public string? BindingAttributeSyntax { get; init; }
}
