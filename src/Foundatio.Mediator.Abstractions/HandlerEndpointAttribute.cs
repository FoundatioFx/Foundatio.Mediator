namespace Foundatio.Mediator;

/// <summary>
/// Marks a handler method for endpoint generation and allows customizing the generated endpoint.
/// When applied to a class, the settings apply to all handler methods in that class unless overridden.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class HandlerEndpointAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerEndpointAttribute"/> class.
    /// </summary>
    public HandlerEndpointAttribute()
    {
    }

    /// <summary>
    /// Gets or sets the HTTP method (GET, POST, PUT, DELETE, PATCH).
    /// When null, the HTTP method is inferred from the message type name:
    /// Get*/Find*/Search*/List*/Query* -> GET, Create*/Add*/New* -> POST,
    /// Update*/Edit*/Modify*/Set* -> PUT, Delete*/Remove* -> DELETE, Patch* -> PATCH.
    /// </summary>
    public string? HttpMethod { get; set; }

    /// <summary>
    /// Gets or sets the route template for this endpoint.
    /// Use {propertyName} for route parameters that match message properties.
    /// Use a leading <c>/</c> to create an absolute route that bypasses both the global and category prefixes (e.g., <c>"/status"</c> routes to <c>/status</c>).
    /// When null, the route is generated from the category's RoutePrefix and message properties.
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    /// Gets or sets the endpoint name for OpenAPI (operationId).
    /// When null, defaults to the message type name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the endpoint summary for OpenAPI.
    /// When null, extracted from the handler method's XML documentation summary.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets the endpoint description for OpenAPI.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the tags for grouping endpoints in OpenAPI.
    /// When null, uses the HandlerCategory attribute's Name property.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Gets or sets whether to exclude this handler from endpoint generation.
    /// When true, no endpoint will be generated for this handler.
    /// </summary>
    public bool Exclude { get; set; }

    /// <summary>
    /// Gets or sets the endpoint filter types for this endpoint.
    /// Each type must implement <c>Microsoft.AspNetCore.Http.IEndpointFilter</c>.
    /// These filters are additive to any global or category-level filters.
    /// </summary>
    public Type[]? EndpointFilters { get; set; }

    /// <summary>
    /// Gets or sets additional HTTP status codes this endpoint can produce.
    /// Used to generate <c>.ProducesProblem(statusCode)</c> calls for OpenAPI documentation.
    /// Only error/problem status codes (4xx, 5xx) should be listed here; the success
    /// <c>.Produces&lt;T&gt;(200/201)</c> is generated automatically from the return type.
    /// </summary>
    /// <example>
    /// <code>
    /// [HandlerEndpoint(ProducesStatusCodes = [404, 422])]
    /// public Result&lt;Order&gt; Handle(GetOrder query) => ...;
    /// </code>
    /// </example>
    public int[]? ProducesStatusCodes { get; set; }

    /// <summary>
    /// Gets or sets the streaming format for handlers that return <c>IAsyncEnumerable&lt;T&gt;</c>.
    /// When set to <see cref="EndpointStreaming.ServerSentEvents"/>, the generated endpoint wraps
    /// the result with <c>TypedResults.ServerSentEvents()</c> for real-time server-to-client push.
    /// Defaults to <see cref="EndpointStreaming.Default"/> (JSON array streaming).
    /// </summary>
    public EndpointStreaming Streaming { get; set; } = EndpointStreaming.Default;

    /// <summary>
    /// Gets or sets the SSE event type name used when <see cref="Streaming"/> is
    /// <see cref="EndpointStreaming.ServerSentEvents"/>. Clients receive this as the
    /// <c>event:</c> field in the SSE stream. When null, no explicit event type is set and
    /// the browser <c>EventSource</c> API fires the default <c>"message"</c> event.
    /// </summary>
    public string? SseEventType { get; set; }
}
