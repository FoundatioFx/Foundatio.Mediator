namespace Foundatio.Mediator;

/// <summary>
/// Groups all endpoints in a handler class under a shared route prefix, OpenAPI tag, and endpoint filters.
/// Applied to handler classes to define the route prefix and default settings for all endpoints in the class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class HandlerEndpointGroupAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerEndpointGroupAttribute"/> class.
    /// When no name is provided, the group name is auto-derived from the handler class name
    /// (e.g., <c>AuthHandler</c> → <c>"Auth"</c>).
    /// </summary>
    public HandlerEndpointGroupAttribute() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerEndpointGroupAttribute"/> class.
    /// </summary>
    /// <param name="name">The group name used for API grouping (e.g., "Products", "Orders").</param>
    public HandlerEndpointGroupAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets or sets the group name used for API grouping and OpenAPI tags.
    /// When null, auto-derived from the handler class name (e.g., <c>AuthHandler</c> → <c>"Auth"</c>).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the route prefix for all endpoints in this group.
    /// This is relative to the global <c>EndpointRoutePrefix</c>.
    /// Use a leading <c>/</c> to create an absolute route that bypasses the global prefix (e.g., <c>"/health"</c> routes to <c>/health</c> instead of <c>/api/health</c>).
    /// </summary>
    public string? RoutePrefix { get; set; }

    /// <summary>
    /// Gets or sets the OpenAPI tags for all endpoints in this group.
    /// When null, the <see cref="Name"/> is used as a single tag.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Gets or sets the endpoint filter types for all endpoints in this group.
    /// Each type must implement <c>Microsoft.AspNetCore.Http.IEndpointFilter</c>.
    /// These filters are additive to any global-level filters and run before endpoint-level filters.
    /// </summary>
    public Type[]? EndpointFilters { get; set; }
}
