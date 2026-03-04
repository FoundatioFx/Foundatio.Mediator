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
[HandlerCategory("Products", RoutePrefix = "products")]
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
| `Update*`, `Edit*`, `Modify*`, `Set*`, `Change*` | PUT |
| `Delete*`, `Remove*` | DELETE |
| `Patch*` | PATCH |
| Default | POST |

## Route Generation

Routes are generated based on:
1. The `[HandlerEndpoint(Route = "...")]` attribute if specified
2. Otherwise: category route prefix + route parameters from message properties

### Route Prefix Architecture

Endpoint routes are composed from three levels, using ASP.NET Core's nested `MapGroup()` pattern:

| Level | Source | Default | Example |
| ----- | ------ | ------- | ------- |
| Global prefix | `[assembly: MediatorConfiguration(EndpointRoutePrefix = "...")]` | `"api"` | `"api"` |
| Category prefix | `[HandlerCategory("Products", RoutePrefix = "...")]` | Derived from class name | `"products"` |
| Endpoint route | `[HandlerEndpoint(Route = "...")]` or auto-generated from message properties | Auto-generated | `"/{productId}"` |

The generator creates nested route groups, and ASP.NET Core concatenates them into the final path:

```text
endpoints.MapGroup("api")           // Global prefix
    → rootGroup.MapGroup("products")    // Category prefix
        → productsGroup.MapGet("/{productId}", ...)  // Endpoint route
```

This produces the final route: **`/api/products/{productId}`**

Here are examples of how the global and category prefixes combine:

```text
EndpointRoutePrefix = "api"   +  RoutePrefix = "products"     →  /api/products
EndpointRoutePrefix = "api"   +  RoutePrefix = "v2/products"  →  /api/v2/products
EndpointRoutePrefix = ""      +  RoutePrefix = "products"     →  /products
EndpointRoutePrefix = "api"   +  RoutePrefix = "api/products" →  /api/api/products  ⚠️ Wrong!
```

> **Important:** Category `RoutePrefix` values without a leading `/` are **relative** — they nest under the global `EndpointRoutePrefix`. Don't repeat the global prefix in category prefixes (e.g. `"api/products"` when the global is already `"api"`) or you'll get doubled paths like `/api/api/...`. The compiler emits warning **FMED015** if it detects this mistake.

### Absolute Routes (Bypassing Prefixes)

Use a leading `/` to create an absolute route that bypasses parent prefixes, matching ASP.NET Core MVC's attribute routing convention where `[Route("/health")]` is absolute:

**Category-level bypass** — skip the global `EndpointRoutePrefix`:

```csharp
[assembly: MediatorConfiguration(EndpointRoutePrefix = "api")]

// Routes to /health, NOT /api/health (leading / = absolute)
[HandlerCategory("Health", RoutePrefix = "/health")]
public class HealthHandler { ... }
```

**Endpoint-level bypass** — skip both the global and category prefixes:

```csharp
[assembly: MediatorConfiguration(EndpointRoutePrefix = "api")]

[HandlerCategory("Products", RoutePrefix = "products")]
public class ProductHandler
{
    // Routes to /status, NOT /api/products/status (leading / = absolute)
    [HandlerEndpoint(Route = "/status")]
    public string Handle(GetStatus query) => "ok";
}
```

This is useful for health checks, status endpoints, or any route that should live outside the normal API hierarchy.

To disable the global prefix entirely, set `EndpointRoutePrefix = ""`:

```csharp
[assembly: MediatorConfiguration(
    EndpointDiscovery = EndpointDiscovery.All,
    EndpointRoutePrefix = ""  // No global prefix
)]
```

### Custom Routes and HTTP Methods

Use `[HandlerEndpoint]` to override the generated route and/or HTTP method for individual handlers:

```csharp
[HandlerCategory("Todos", RoutePrefix = "todos")]
public class TodoHandler
{
    // POST /api/todos/{todoId}/complete
    [HandlerEndpoint(Route = "{todoId}/complete", HttpMethod = "POST")]
    public Task<Result> HandleAsync(CompleteTodo command) { ... }

    // PATCH /api/todos/{todoId}
    [HandlerEndpoint(HttpMethod = "PATCH")]
    public Task<Result<Todo>> HandleAsync(PatchTodo command) { ... }

    // GET /api/todos/search (custom route, inferred GET from name)
    [HandlerEndpoint(Route = "search")]
    public Task<Result<List<Todo>>> HandleAsync(SearchTodos query) { ... }
}
```

The `Route` property is relative to the category's `RoutePrefix`. The `HttpMethod` property accepts `"GET"`, `"POST"`, `"PUT"`, `"DELETE"`, or `"PATCH"`.

### Excluding Handlers from Endpoint Generation

Use `[HandlerEndpoint(Exclude = true)]` to prevent endpoint generation for specific handlers:

```csharp
public class ProductHandler
{
    // This handler will NOT have an endpoint generated
    [HandlerEndpoint(Exclude = true)]
    public Task<Result> HandleAsync(InternalSync command) { ... }

    // This handler gets a normal endpoint
    public Task<Result<Product>> HandleAsync(GetProduct query) { ... }
}
```

Apply at the class level to exclude all methods in a handler:

```csharp
[HandlerEndpoint(Exclude = true)]
public class InternalHandler
{
    // No endpoints generated for any method
    public Task<Result> HandleAsync(ProcessBatch command) { ... }
    public Task<Result> HandleAsync(CleanupOldData command) { ... }
}
```

> **Tip:** If you manually map endpoints for some handlers using `app.MapGet(...)` etc., exclude them from auto-generation with `[HandlerEndpoint(Exclude = true)]` to avoid route conflicts at startup.

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

### File Downloads

When a handler returns `Result<FileResult>`, the endpoint automatically uses `Results.File()` instead of `Results.Ok()`:

```csharp
public class ReportHandler
{
    [HandlerEndpoint(HttpMethod = "GET", Route = "/reports/{id}")]
    public async Task<Result<FileResult>> HandleAsync(
        GetReport query, IReportService reports, CancellationToken ct)
    {
        var stream = await reports.GeneratePdfAsync(query.Id, ct);
        return Result.File(stream, "application/pdf", $"report-{query.Id}.pdf");
    }
}
```

The generated endpoint maps the `FileResult` to a file response with the correct content type and optional `Content-Disposition: attachment` header (when `FileName` is set).

## Customization Attributes

### `[HandlerCategory]`

Groups handlers and sets route prefixes:

```csharp
[HandlerCategory("Products", RoutePrefix = "products")]
public class ProductHandler { ... }
```

Properties:
- `Name` (constructor) - Category name for OpenAPI tags
- `RoutePrefix` - Route prefix for all handlers in this class (relative to global prefix; use leading `/` for absolute)

### `[HandlerEndpoint]`

Customizes individual endpoint behavior:

```csharp
public class ProductHandler
{
    [HandlerEndpoint(
        HttpMethod = "POST",
        Route = "bulk",
        Name = "BulkCreateProducts",
        Summary = "Creates multiple products at once")]
    public Task<Result<List<Product>>> HandleAsync(BulkCreateProducts command) { ... }

    [HandlerEndpoint(Exclude = true)]  // Not exposed as endpoint
    public Task<Result> HandleAsync(InternalProductSync command) { ... }
}
```

Properties:
- `HttpMethod` - Override the inferred HTTP method
- `Route` - Custom route template (relative to category prefix; use leading `/` for absolute)
- `Name` - OpenAPI operation ID
- `Summary` - Override XML doc summary
- `Description` - OpenAPI description
- `Tags` - Override category tags
- `Exclude` - Exclude from endpoint generation
- `EndpointFilters` - Endpoint filter types
- `ProducesStatusCodes` - Additional HTTP status codes for OpenAPI documentation

### OpenAPI Error Responses

When a handler returns `Result<T>` or `Result`, the generator automatically scans the method body for `Result` factory method calls (`Result.NotFound()`, `Result.Invalid()`, etc.) and emits matching `.ProducesProblem()` metadata — no configuration needed:

```csharp
public class OrderHandler
{
    public Result<OrderView> Handle(GetOrder query)
    {
        if (query.Id == null)
            return Result<OrderView>.NotFound("Order not found");  // → .ProducesProblem(404)

        if (!IsValid(query))
            return Result<OrderView>.Invalid("Bad request");       // → .ProducesProblem(422)

        return new OrderView(query.Id, "Test");
    }
    // Auto-generates:
    //   .Produces<OrderView>(200)
    //   .ProducesProblem(404)
    //   .ProducesProblem(422)
}
```

The following `Result` factory methods are detected:

| Method | Status Code |
| --- | --- |
| `BadRequest()` | 400 |
| `Unauthorized()` | 401 |
| `Forbidden()` | 403 |
| `NotFound()` | 404 |
| `Conflict()` | 409 |
| `Invalid()` | 422 |
| `Error()` / `CriticalError()` | 500 |
| `Unavailable()` | 503 |

#### Explicit Override

To override auto-detection (e.g., when error results come from injected services), use `ProducesStatusCodes`:

```csharp
public class OrderHandler
{
    [HandlerEndpoint(ProducesStatusCodes = [404, 422])]
    public Result<OrderView> Handle(GetOrder query) => ...;
    // Uses only the explicitly declared codes, ignoring method body analysis
}
```

Class-level settings apply to all methods unless overridden at method level:

```csharp
[HandlerEndpoint(ProducesStatusCodes = [400, 500])]
public class ProductHandler
{
    // Inherits [400, 500] from class
    public Result<ProductView> Handle(CreateProduct command) => ...;

    // Overrides with its own set
    [HandlerEndpoint(ProducesStatusCodes = [404, 409, 422])]
    public Result<ProductView> Handle(UpdateProduct command) => ...;
}
```

## Authentication & Authorization

Foundatio.Mediator provides unified authorization that works for **both** HTTP endpoints and direct `mediator.InvokeAsync()` calls. Authorization is configured via `[HandlerAuthorize]` and `[HandlerAllowAnonymous]` attributes, or globally via the assembly attribute.

### Handler-Level Authorization

Use `[HandlerAuthorize]` on a handler class or method to require authorization:

```csharp
[HandlerAuthorize]
public class SecureHandler
{
    public Task<Result<Secret>> HandleAsync(GetSecret query) { ... }
}

// With roles and policies
[HandlerAuthorize(Roles = ["Admin", "Manager"], Policies = ["CanEditProducts"])]
public class AdminHandler
{
    public Task<Result> HandleAsync(DeleteProduct command) { ... }
}
```

For Result-returning handlers, unauthorized requests receive `Result.Unauthorized()` or `Result.Forbidden()`. For non-Result handlers, an `UnauthorizedAccessException` is thrown.

### Global Default

Set a default auth requirement for all handlers using the assembly attribute:

```csharp
[assembly: MediatorConfiguration(
    EndpointDiscovery = EndpointDiscovery.All,
    AuthorizationRequired = true
)]
```

### Opting Out

Use `[HandlerAllowAnonymous]` to bypass authorization on specific handlers when global auth is enabled:

```csharp
[HandlerAllowAnonymous]
public class PublicHandler
{
    public Task<Result<Status>> HandleAsync(HealthCheck query) { ... }
}
```

ASP.NET Core's `[AllowAnonymous]` attribute is also recognized.

### Category Level

Override at the category level:

```csharp
[HandlerCategory("Admin", RoutePrefix = "admin")]
[HandlerAuthorize(Policies = ["AdminOnly"])]
public class AdminHandler { ... }

[HandlerCategory("Public", RoutePrefix = "public")]
[HandlerAllowAnonymous]
public class PublicHandler { ... }
```

### Endpoint Level

Use `[HandlerAuthorize]` on individual handler methods for endpoint-specific authorization:

```csharp
[HandlerAuthorize(Roles = ["Admin", "Manager"])]
public Task<Result> HandleAsync(DeleteProduct command) { ... }
```

### Custom Authorization Services

The authorization system is extensible via two interfaces:

- **`IAuthorizationContextProvider`** — Provides the `ClaimsPrincipal` for the current request. In ASP.NET Core apps, this is auto-registered to read from `HttpContext.User`.
- **`IHandlerAuthorizationService`** — Performs the authorization check. The default implementation checks roles and policies against the principal's claims.

You can replace either service via DI to customize behavior.

## Discovery Modes

Control which handlers generate endpoints using the assembly attribute:

### All Mode

All handlers with valid endpoint info generate endpoints:

```csharp
[assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]
```

Use `[HandlerEndpoint(Exclude = true)]` to opt out specific handlers.

### Explicit Mode

Only handlers with `[HandlerEndpoint]` or `[HandlerCategory]` attributes generate endpoints:

```csharp
[assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.Explicit)]
```

### None Mode (Default)

No endpoints are generated. This is the default when `EndpointDiscovery` is not set.

## Project Name Configuration

Control the generated extension method name with the `ProjectName` property on `MediatorConfiguration`:

```csharp
[assembly: MediatorConfiguration(
    ProjectName = "Products",
    EndpointDiscovery = EndpointDiscovery.All
)]
```

This generates:
- `MapProductsEndpoints()` extension method
- `MediatorEndpointExtensions_Products` class
- `MediatorEndpointResultMapper_Products` class

Without this setting, the assembly name is used (with dots/dashes converted to underscores).

## Generated Code Example

For a handler like this:

```csharp
[assembly: MediatorConfiguration(
    EndpointDiscovery = EndpointDiscovery.All
)]

[HandlerCategory("Products")]
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
        var rootGroup = endpoints.MapGroup("api");
        var productsGroup = rootGroup.MapGroup("products").WithTags("Products");

        // POST /api/products - CreateProduct
        productsGroup.MapPost("/", async ([FromBody] CreateProduct message,
            IMediator mediator, CancellationToken ct) =>
        {
            var result = await ProductHandler_CreateProduct_Handler.HandleAsync(mediator, message, ct);
            return MediatorEndpointResultMapper_Products.ToHttpResult(result);
        })
        .WithName("CreateProduct")
        .WithSummary("Creates a new product")
        .Produces<Product>(201);

        // GET /api/products/{productId} - GetProduct
        productsGroup.MapGet("/{productId}", (string productId,
            IMediator mediator, CancellationToken ct) =>
        {
            var message = new GetProduct(productId);
            var result = ProductHandler_GetProduct_Handler.Handle(mediator, message, ct);
            return MediatorEndpointResultMapper_Products.ToHttpResult(result);
        })
        .WithName("GetProduct")
        .WithSummary("Gets a product by ID")
        .Produces<Product>(200);

        return endpoints;
    }
}
```

## Events and Notifications

Handlers for event/notification types are automatically excluded from endpoint generation:

- Types implementing `INotification`, `IEvent`, `IDomainEvent`, or `IIntegrationEvent`
- Handler classes named `*EventHandler` or `*NotificationHandler`
- Types with names ending in common event suffixes: `Created`, `Updated`, `Deleted`, `Changed`, `Removed`, `Added`, `Event`, `Notification`, `Published`, `Occurred`, `Happened`, `Started`, `Completed`, `Failed`, `Cancelled`, `Expired`

## Troubleshooting

### Endpoints Not Generated

1. Ensure your project references ASP.NET Core (has `Microsoft.AspNetCore.Routing`)
2. Check `[assembly: MediatorConfiguration(EndpointDiscovery = ...)]` — default is `None` (no endpoints). Set to `All` or `Explicit`.
3. In `Explicit` mode, handlers need `[HandlerEndpoint]` or `[HandlerCategory]`
4. Verify the handler isn't excluded via `[HandlerEndpoint(Exclude = true)]`

### XML Summaries Not Appearing

1. Enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
2. Rebuild the project completely

### Route Conflicts

When multiple handlers generate the same route, the generator automatically differentiates them using the message type name in kebab-case.
