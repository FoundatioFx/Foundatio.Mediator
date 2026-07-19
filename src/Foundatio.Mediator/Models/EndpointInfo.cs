using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

/// <summary>
/// Contains metadata for generating a minimal API endpoint from a handler.
/// </summary>
internal readonly record struct EndpointInfo
{
    /// <summary>
    /// The HTTP method (GET, POST, PUT, DELETE, PATCH, QUERY).
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
    /// The ASP.NET Core endpoint display name (emitted via <c>.WithDisplayName(...)</c>).
    /// Null when unset; ASP.NET Core then derives a display name from the route pattern.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// When true, the generated endpoint is hidden from the OpenAPI description via
    /// <c>.ExcludeFromDescription()</c> while remaining routable.
    /// </summary>
    public bool ExcludeFromDescription { get; init; }

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
    /// Authorization policy names required by the group (emitted as <c>.RequireAuthorization("policy")</c>
    /// on the group). From <c>[HandlerEndpointGroup]</c> or a referenced <c>[MediatorEndpointGroup]</c>.
    /// </summary>
    public EquatableArray<string> GroupPolicies { get; init; }

    /// <summary>
    /// When true, the group is hidden from the OpenAPI description via <c>.ExcludeFromDescription()</c>
    /// on the group builder while remaining routable.
    /// </summary>
    public bool GroupExcludeFromDescription { get; init; }

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
    /// Whether the message should be bound from body (POST/PUT/PATCH/QUERY).
    /// </summary>
    public bool BindFromBody { get; init; }

    /// <summary>
    /// Whether the body-bound message type defines its own minimal-API custom binding
    /// (implements <c>IBindableFromHttpContext&lt;TSelf&gt;</c> or declares a public static
    /// <c>BindAsync</c> method). When true, the generated endpoint omits <c>[FromBody]</c> so
    /// ASP.NET Core uses the type's custom binding instead of JSON body binding.
    /// </summary>
    public bool MessageHasCustomBinding { get; init; }

    /// <summary>
    /// Whether the message should be bound from a multipart/form-data request.
    /// Set when a POST/PUT/PATCH/QUERY message exposes an <c>IFormFile</c>/<c>IFormFileCollection</c>/<c>IFormCollection</c>
    /// property. Mutually exclusive with <see cref="BindFromBody"/>; the endpoint also emits <c>.DisableAntiforgery()</c>.
    /// </summary>
    public bool BindFromForm { get; init; }

    /// <summary>
    /// Form parameters (files bound by name with no attribute; other fields with <c>[FromForm]</c>)
    /// merged into the message when <see cref="BindFromForm"/> is true.
    /// </summary>
    public EquatableArray<EndpointParameterInfo> FormParameters { get; init; }

    /// <summary>
    /// Handler-level override (method then class) for disabling antiforgery validation on a form endpoint.
    /// <c>null</c> when unset — the generator then falls back to the assembly-level default. Only meaningful
    /// when <see cref="BindFromForm"/> is true.
    /// </summary>
    public bool? DisableAntiforgery { get; init; }

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
    /// Subset of <see cref="ProducesStatusCodes"/> that represent validation problems
    /// (auto-detected from <c>Result.Invalid()</c> factory calls). These are emitted as
    /// <c>.ProducesValidationProblem(statusCode)</c> instead of <c>.ProducesProblem(statusCode)</c>
    /// so the generated metadata matches the <c>HttpValidationProblemDetails</c> body the mapper
    /// actually returns for <see cref="ResultStatus.Invalid"/>.
    /// </summary>
    public EquatableArray<int> ValidationProblemStatusCodes { get; init; }

    /// <summary>
    /// Request content types accepted by this endpoint when a request body is bound.
    /// </summary>
    public EquatableArray<string> AcceptsContentTypes { get; init; }

    /// <summary>
    /// Response content types used for generated success response metadata.
    /// </summary>
    public EquatableArray<string> ProducesContentTypes { get; init; }

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
    /// Success HTTP status codes used to generate response metadata.
    /// Explicit <c>[HandlerEndpoint(SuccessStatusCodes = ...)]</c> values take precedence over auto-detected values.
    /// </summary>
    public EquatableArray<int> SuccessStatusCodes { get; init; }

    /// <summary>
    /// Endpoint convention attributes that implement <c>IEndpointConvention&lt;TBuilder&gt;</c>.
    /// These are instantiated and called with the endpoint or group builder at startup.
    /// </summary>
    public EquatableArray<EndpointConventionInfo> Conventions { get; init; }
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
    /// Whether this parameter is bound from a multipart/form-data request.
    /// File parameters (<c>IFormFile</c>/<c>IFormFileCollection</c>/<c>IFormCollection</c>) bind by name
    /// with no attribute; other form fields carry a <c>[FromForm]</c> <see cref="BindingAttributeSyntax"/>.
    /// </summary>
    public bool IsFormParameter { get; init; }

    /// <summary>
    /// The full attribute syntax to emit on the endpoint lambda parameter
    /// (e.g., <c>[Microsoft.AspNetCore.Mvc.FromHeader(Name = "X-Tenant-Id")]</c>).
    /// Null when the parameter uses default binding (route or query convention).
    /// </summary>
    public string? BindingAttributeSyntax { get; init; }
}
