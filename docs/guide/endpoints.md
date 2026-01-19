# Automatic Endpoint Generation

Foundatio Mediator can automatically generate ASP.NET Core Minimal API endpoints from your handlers. This eliminates boilerplate code and keeps your API definitions in sync with your handler logic.

## Overview

When your project references ASP.NET Core, the source generator automatically creates endpoint registration code that:
- Maps handlers to HTTP endpoints based on conventions
- Binds request parameters from route, query string, or body
- Maps `Result<T>` return types to appropriate HTTP status codes
- Generates OpenAPI metadata from XML documentation comments

## Getting Started

### 1. Enable XML Documentation

To get endpoint summaries from your handler's XML doc comments, enable documentation generation:

```xml
<PropertyGroup>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
</PropertyGroup>
```

### 2. Add Category to Handlers

Use `[HandlerCategory]` to group endpoints and set route prefixes:

```csharp
[HandlerCategory("Products", RoutePrefix = "/api/products")]
public class ProductHandler
{
    /// <summary>
    /// Creates a new product in the catalog.
    /// </summary>
    public Task<Result<Product>> HandleAsync(CreateProduct command) { ... }

    /// <summary>
    /// Retrieves a product by ID.
    /// </summary>
    public Result<Product> Handle(GetProduct query) { ... }
}
```

### 3. Map the Endpoints

In your startup code, call the generated extension method:

```csharp
var app = builder.Build();

// Map endpoints from all modules
app.MapProductsEndpoints();      // From Products.Module
app.MapOrdersEndpoints();        // From Orders.Module

app.Run();
```

## HTTP Method Inference

The HTTP method is inferred from the message type name:

| Message Name Pattern | HTTP Method |
|---------------------|-------------|
| `Get*`, `Find*`, `Search*`, `List*`, `Query*` | GET |
| `Create*`, `Add*`, `New*` | POST |
| `Update*`, `Edit*`, `Modify*`, `Set*` | PUT |
| `Delete*`, `Remove*` | DELETE |
| `Patch*`, `Change*` | PATCH |
| Default | POST |

## Route Generation

Routes are generated based on:
1. The `[HandlerEndpoint(Route = "...")]` attribute if specified
2. Otherwise: category route prefix + route parameters from message properties

### Route Parameters

Properties named `Id` or ending with `Id` automatically become route parameters:

```csharp
// Message
public record GetProduct(string ProductId);

// Generated route: GET /api/products/{productId}
```

### Query Parameters

For GET/DELETE requests, non-ID properties become query parameters:

```csharp
// Message
public record SearchProducts(string? Category, int? MinPrice, int? MaxPrice);

// Generated: GET /api/products?category=...&minPrice=...&maxPrice=...
```

## Parameter Binding

### GET/DELETE Requests

The generator supports two binding patterns:

**`[AsParameters]` binding** (when message has a parameterless constructor):
```csharp
// Message with parameterless constructor
public record GetProducts
{
    public string? Category { get; init; }
    public int Page { get; init; } = 1;
}

// Generated endpoint uses [AsParameters]
productsGroup.MapGet("/", async ([AsParameters] GetProducts message, ...) => ...);
```

**Constructor binding** (when message only has parameterized constructor):
```csharp
// Message with required constructor parameters
public record GetProduct(string ProductId);

// Generated endpoint constructs the message
productsGroup.MapGet("/{productId}", async (string productId, ...) =>
{
    var message = new GetProduct(productId);
    ...
});
```

### POST/PUT/PATCH Requests

Request body is bound using `[FromBody]`:
```csharp
productsGroup.MapPost("/", async ([FromBody] CreateProduct message, ...) => ...);
```

For PUT/PATCH with route parameters, the body is merged with route values:
```csharp
// Message
public record UpdateProduct(string ProductId, string? Name, decimal? Price);

// Generated endpoint
productsGroup.MapPut("/{productId}", async (string productId, [FromBody] UpdateProduct message, ...) =>
{
    var mergedMessage = message with { ProductId = productId };
    ...
});
```

## Result to HTTP Status Mapping

When handlers return `Result<T>` or `Result`, the status is automatically mapped:

| ResultStatus | HTTP Status |
|--------------|-------------|
| `Success` | 200 OK |
| `Created` | 201 Created |
| `NoContent` | 204 No Content |
| `BadRequest` | 400 Bad Request |
| `Invalid` | 422 Unprocessable Entity (ValidationProblem) |
| `NotFound` | 404 Not Found |
| `Unauthorized` | 401 Unauthorized |
| `Forbidden` | 403 Forbidden |
| `Conflict` | 409 Conflict |
| `Error` | 500 Internal Server Error |
| `CriticalError` | 500 Internal Server Error |
| `Unavailable` | 503 Service Unavailable |

## Customization Attributes

### `[HandlerCategory]`

Groups handlers and sets route prefixes:

```csharp
[HandlerCategory("Products", RoutePrefix = "/api/products")]
public class ProductHandler { ... }
```

Properties:
- `Name` (constructor) - Category name for OpenAPI tags
- `RoutePrefix` - Base route for all handlers in this class
- `RequireAuth` - Default auth requirement for handlers in this category
- `Roles` - Default required roles
- `Policy` - Default authorization policy

### `[HandlerEndpoint]`

Customizes individual endpoint behavior:

```csharp
public class ProductHandler
{
    [HandlerEndpoint(
        HttpMethod = "POST",
        Route = "/api/products/bulk",
        Name = "BulkCreateProducts",
        Summary = "Creates multiple products at once")]
    public Task<Result<List<Product>>> HandleAsync(BulkCreateProducts command) { ... }

    [HandlerEndpoint(Exclude = true)]  // Not exposed as endpoint
    public Task<Result> HandleAsync(InternalProductSync command) { ... }
}
```

Properties:
- `HttpMethod` - Override the inferred HTTP method
- `Route` - Custom route template
- `Name` - OpenAPI operation ID
- `Summary` - Override XML doc summary
- `Description` - OpenAPI description
- `Tags` - Override category tags
- `Exclude` - Exclude from endpoint generation
- `RequireAuth` - Require authentication
- `Roles` - Required roles
- `Policy` - Authorization policy name
- `Policies` - Multiple authorization policies

## Authentication & Authorization

### Global Default

Set a default auth requirement for all endpoints:

```xml
<PropertyGroup>
    <MediatorEndpointRequireAuth>true</MediatorEndpointRequireAuth>
</PropertyGroup>
```

### Category Level

Override at the category level:

```csharp
[HandlerCategory("Admin", RoutePrefix = "/api/admin", RequireAuth = true, Policy = "AdminOnly")]
public class AdminHandler { ... }

[HandlerCategory("Public", RoutePrefix = "/api/public", RequireAuth = false)]
public class PublicHandler { ... }
```

### Endpoint Level

Override for specific endpoints:

```csharp
[HandlerEndpoint(RequireAuth = true, Roles = new[] { "Admin", "Manager" })]
public Task<Result> HandleAsync(DeleteProduct command) { ... }
```

## Discovery Modes

Control which handlers generate endpoints:

### All Mode (Default)

All handlers with valid endpoint info generate endpoints:

```xml
<PropertyGroup>
    <MediatorEndpointDiscovery>All</MediatorEndpointDiscovery>
</PropertyGroup>
```

Use `[HandlerEndpoint(Exclude = true)]` to opt out specific handlers.

### Explicit Mode

Only handlers with `[HandlerEndpoint]` attribute generate endpoints:

```xml
<PropertyGroup>
    <MediatorEndpointDiscovery>Explicit</MediatorEndpointDiscovery>
</PropertyGroup>
```

## Project Name Configuration

Control the generated extension method name with `MediatorProjectName`:

```xml
<PropertyGroup>
    <MediatorProjectName>Products</MediatorProjectName>
</PropertyGroup>
```

This generates:
- `MapProductsEndpoints()` extension method
- `MediatorEndpointExtensions_Products` class
- `MediatorEndpointResultMapper_Products` class

Without this setting, the assembly name is used (with dots/dashes converted to underscores).

## Generated Code Example

For a handler like this:

```csharp
[HandlerCategory("Products", RoutePrefix = "/api/products")]
public class ProductHandler
{
    /// <summary>
    /// Creates a new product
    /// </summary>
    public Task<Result<Product>> HandleAsync(CreateProduct command) { ... }

    /// <summary>
    /// Gets a product by ID
    /// </summary>
    public Result<Product> Handle(GetProduct query) { ... }
}
```

The generator produces:

```csharp
public static class MediatorEndpointExtensions_Products
{
    public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var productsGroup = endpoints.MapGroup("/api/products").WithTags("Products");

        // POST /api/products - CreateProduct
        productsGroup.MapPost("/", async ([FromBody] CreateProduct message,
            IMediator mediator, CancellationToken ct) =>
        {
            var result = await ProductHandler_CreateProduct_Handler.HandleAsync(mediator, message, ct);
            return MediatorEndpointResultMapper_Products.ToHttpResult(result);
        })
        .WithName("CreateProduct")
        .WithSummary("Creates a new product");

        // GET /api/products/{productId} - GetProduct
        productsGroup.MapGet("/{productId}", (string productId,
            IMediator mediator, CancellationToken ct) =>
        {
            var message = new GetProduct(productId);
            var result = ProductHandler_GetProduct_Handler.Handle(mediator, message, ct);
            return MediatorEndpointResultMapper_Products.ToHttpResult(result);
        })
        .WithName("GetProduct")
        .WithSummary("Gets a product by ID");

        return endpoints;
    }
}
```

## Events and Notifications

Handlers for event/notification types are automatically excluded from endpoint generation:
- Types implementing `INotification`
- Types with names ending in `Event`, `Notification`, `Created`, `Updated`, `Deleted`

## Troubleshooting

### Endpoints Not Generated

1. Ensure your project references ASP.NET Core (has `Microsoft.AspNetCore.Routing`)
2. Check `MediatorEndpointDiscovery` setting - in `Explicit` mode, handlers need `[HandlerEndpoint]`
3. Verify the handler isn't excluded via `[HandlerEndpoint(Exclude = true)]`

### XML Summaries Not Appearing

1. Enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
2. Rebuild the project completely

### Route Conflicts

When multiple handlers generate the same route, the generator automatically differentiates them using the message type name in kebab-case.
