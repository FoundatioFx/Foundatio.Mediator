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
    /// The category name for API grouping (from HandlerCategoryAttribute).
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// The route prefix from the category (e.g., "/api/products").
    /// </summary>
    public string? CategoryRoutePrefix { get; init; }

    /// <summary>
    /// Route parameters extracted from the message type properties.
    /// </summary>
    public EquatableArray<EndpointParameterInfo> RouteParameters { get; init; }

    /// <summary>
    /// Query parameters for GET/DELETE requests.
    /// </summary>
    public EquatableArray<EndpointParameterInfo> QueryParameters { get; init; }

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
    /// Whether this handler should generate an endpoint.
    /// </summary>
    public bool GenerateEndpoint { get; init; }

    /// <summary>
    /// Whether this endpoint requires authentication.
    /// </summary>
    public bool RequireAuth { get; init; }

    /// <summary>
    /// Required roles for this endpoint (any of).
    /// </summary>
    public EquatableArray<string> Roles { get; init; }

    /// <summary>
    /// Required authorization policies for this endpoint (all must be satisfied).
    /// </summary>
    public EquatableArray<string> Policies { get; init; }
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
}
