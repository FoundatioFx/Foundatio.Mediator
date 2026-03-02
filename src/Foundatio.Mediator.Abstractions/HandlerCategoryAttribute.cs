namespace Foundatio.Mediator;

/// <summary>
/// Specifies the API category/tag for grouping endpoints.
/// Applied to handler classes to define the route prefix and default settings for all endpoints in the class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class HandlerCategoryAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerCategoryAttribute"/> class.
    /// </summary>
    /// <param name="name">The category name used for API grouping (e.g., "Products", "Orders").</param>
    public HandlerCategoryAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the category name used for API grouping and OpenAPI tags.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the route prefix for all endpoints in this category.
    /// For example, "/api/products" will prefix all product-related endpoints.
    /// </summary>
    public string? RoutePrefix { get; set; }

    /// <summary>
    /// Gets or sets the endpoint filter types for all endpoints in this category.
    /// Each type must implement <c>Microsoft.AspNetCore.Http.IEndpointFilter</c>.
    /// These filters are additive to any global-level filters and run before endpoint-level filters.
    /// </summary>
    public Type[]? EndpointFilters { get; set; }
}
