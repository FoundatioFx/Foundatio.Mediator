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
    /// Gets or sets whether this endpoint requires authentication.
    /// When null, uses the category's RequireAuth setting, then the global MediatorEndpointRequireAuth setting.
    /// </summary>
    public bool RequireAuth { get; set; }

    /// <summary>
    /// Gets or sets whether RequireAuth was explicitly set.
    /// This is used internally to determine if the value should override defaults.
    /// </summary>
    internal bool RequireAuthSet { get; set; }

    /// <summary>
    /// Gets or sets the required roles for this endpoint.
    /// Multiple roles are treated as "any of" (user must have at least one).
    /// </summary>
    public string[]? Roles { get; set; }

    /// <summary>
    /// Gets or sets the required authorization policy for this endpoint.
    /// </summary>
    public string? Policy { get; set; }

    /// <summary>
    /// Gets or sets multiple required authorization policies for this endpoint.
    /// All policies must be satisfied.
    /// </summary>
    public string[]? Policies { get; set; }
}
