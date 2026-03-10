# Endpoint Generation

Foundatio Mediator automatically generates ASP.NET Core Minimal API endpoints from your handlers. Write your handlers as plain message-in/result-out methods, call `.MapMediatorEndpoints()`, and you have a fully functional API — with smart route conventions, HTTP method inference, and OpenAPI metadata — all without writing a single controller or endpoint definition.

Because your handler logic is completely decoupled from HTTP, it's trivially testable: just send a message through the mediator and assert the result. The endpoint layer is a thin, generated projection that you never maintain by hand.

## Quick Start

Write a handler:

```csharp
public class ProductHandler
{
    public Task<Result<Product>> HandleAsync(CreateProduct command) { ... }
    public Result<Product> Handle(GetProduct query) { ... }
    public Result<List<Product>> Handle(GetProducts query) { ... }
    public Task<Result<Product>> HandleAsync(UpdateProduct command) { ... }
    public Task<Result> HandleAsync(DeleteProduct command) { ... }
}
```

Map it in your startup:

```csharp
var app = builder.Build();
app.MapMediatorEndpoints();
app.Run();
```

That's it. You now have:

```text
POST   /api/products              → CreateProduct
GET    /api/products/{productId}  → GetProduct
GET    /api/products              → GetProducts
PUT    /api/products/{productId}  → UpdateProduct
DELETE /api/products/{productId}  → DeleteProduct
```

No attributes required. The source generator infers everything from your message names and properties:

- **HTTP method** — from the message name prefix (`Get*` → GET, `Create*` → POST, `Update*` → PUT, `Delete*` → DELETE, etc.)
- **Route** — from the message name (minus the verb prefix), **auto-pluralized** to follow REST conventions, with message properties (names ending in `Id` become route parameters)
- **Parameter binding** — ID properties go in the route, other properties become query parameters (GET/DELETE) or body (POST/PUT/PATCH)
- **OpenAPI metadata** — operation names, status codes, and even error responses are auto-detected from your `Result` factory calls
- **Result mapping** — `Result<T>` return values are automatically converted to the correct HTTP status codes

### Why This Matters

This architecture gives you a **loosely coupled, message-oriented application** with close to zero boilerplate. Your handlers don't know they're behind HTTP — they receive a message and return a result. This means:

- **Testing is trivial** — handlers are plain methods with no framework code, so you can call them directly in a unit test. No mediator, no `HttpClient`, no test server, no request serialization.
- **Transport-agnostic** — the same handler works through HTTP endpoints, direct mediator calls, background jobs, or SignalR — the handler doesn't care.
- **Always in sync** — endpoints are generated from your handler code, so your API can never drift from your business logic.

## Streaming Endpoints & Server-Sent Events

Handlers that return `IAsyncEnumerable<T>` automatically become streaming HTTP endpoints. Combined with `SubscribeAsync`, you can push real-time domain events to the browser in just a few lines:

```csharp
public record GetStream;
public record ClientEvent(string EventType, object Data);

public class EventHandler(IMediator mediator)
{
    [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
    public async IAsyncEnumerable<ClientEvent> Handle(
        GetStream message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in mediator.SubscribeAsync<INotification>(
            cancellationToken: cancellationToken))
        {
            yield return new ClientEvent(evt.GetType().Name, evt);
        }
    }
}
```

That generates `GET /api/event/stream` as an SSE endpoint. Any browser client can subscribe:

```javascript
const source = new EventSource('/api/event/stream');
source.onmessage = (e) => {
    const event = JSON.parse(e.data);
    console.log(event.eventType, event.data);
};
```

Whenever any handler publishes a notification, every connected SSE client receives it instantly. Zero polling, zero WebSocket infrastructure.

### JSON Array Streaming

Without the SSE attribute, streaming handlers return a JSON array streamed incrementally — useful for large datasets that you don't want to buffer in memory:

```csharp
public class ReportHandler
{
    public async IAsyncEnumerable<SalesRecord> HandleAsync(
        GetSalesStream query,
        ISalesRepository repository,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var record in repository.GetSalesAsync(query.Year, cancellationToken))
        {
            yield return record;
        }
    }
}
```

This generates a `GET /api/report/sales-stream` endpoint that streams results as they're produced — ASP.NET Core sends each item to the client without waiting for the full result set.

| `Streaming` Value | Behavior |
| --- | --- |
| `EndpointStreaming.Default` | JSON array streaming (default for `IAsyncEnumerable<T>` handlers) |
| `EndpointStreaming.ServerSentEvents` | SSE via `TypedResults.ServerSentEvents()` (.NET 10+) |

| `SseEventType` | Behavior |
| --- | --- |
| `null` (default) | Browser `EventSource` fires the default `message` event |
| `"event"` | Clients listen with `addEventListener('event', ...)` |

::: tip
For a complete guide on streaming patterns including `SubscribeAsync`, dynamic subscriptions, buffering, and real-world examples, see [Streaming Handlers](./streaming-handlers.md).
:::

## Customization Attributes

Everything works out of the box with smart defaults. Attributes are only needed when you want to change a default behavior — group endpoints with a shared route prefix, override a route, change an HTTP method, or exclude a handler from generation.

### `[HandlerEndpointGroup]` — Group Endpoints

`[HandlerEndpointGroup]` is applied to a handler **class** and controls all endpoints in that class as a group. Use it to set a shared route prefix, OpenAPI tag, or endpoint filters for every handler method on the class.

```csharp
[HandlerEndpointGroup("Products")]
public class ProductHandler
{
    public Task<Result<Product>> HandleAsync(CreateProduct command) { ... }
    public Result<Product> Handle(GetProduct query) { ... }
}
```

This changes the routes from the default (derived from the class name) to use the group name:

```text
POST  /api/products              → CreateProduct
GET   /api/products/{productId}  → GetProduct
```

**When do you need `[HandlerEndpointGroup]`?**

- To set a **custom route prefix** different from the class name: `[HandlerEndpointGroup("Products", RoutePrefix = "v2/products")]`
- To set a **shared OpenAPI tag** for grouping in Swagger UI
- To apply **endpoint filters** to all endpoints in the class: `[HandlerEndpointGroup("Orders", EndpointFilters = [typeof(AuditFilter)])]`
- To share auth, filter, or routing configuration across all handler methods in one place

**Properties:**

| Property | Purpose |
| --- | --- |
| `Name` (constructor) | Group name used as the OpenAPI tag and default route prefix (lowercased) |
| `RoutePrefix` | Override the route prefix (relative to global prefix; use leading `/` for absolute) |
| `Tags` | Override the OpenAPI tags (defaults to `Name` as a single tag) |
| `EndpointFilters` | `IEndpointFilter` types applied to all endpoints in this group |

### `[HandlerEndpoint]` — Customize Individual Endpoints

`[HandlerEndpoint]` is applied to a handler **method** (or class to set defaults for all methods) and controls a single endpoint. Use it to override the route, HTTP method, OpenAPI metadata, or exclude a handler from endpoint generation.

```csharp
public class TodoHandler
{
    // Override route and HTTP method for an action endpoint
    [HandlerEndpoint(Route = "{todoId}/complete", HttpMethod = EndpointHttpMethod.Post)]
    public Task<Result> HandleAsync(CompleteTodo command) { ... }

    // Custom OpenAPI metadata
    [HandlerEndpoint(Name = "BulkCreateTodos", Summary = "Creates multiple todos at once")]
    public Task<Result<List<Todo>>> HandleAsync(BulkCreateTodos command) { ... }

    // Exclude from endpoint generation
    [HandlerEndpoint(Exclude = true)]
    public Task<Result> HandleAsync(InternalCleanup command) { ... }
}
```

**When do you need `[HandlerEndpoint]`?**

- To set a **custom route** different from what's auto-generated
- To override the **HTTP method** when conventions don't match (e.g., an action verb that should be POST)
- To add **OpenAPI metadata** (summary, description, operation ID, tags)
- To **exclude** a handler from endpoint generation
- To set a specific **success status code** or explicit **error status codes**
- To configure **SSE streaming** on a streaming handler
- To apply **endpoint filters** to a specific endpoint

**Properties:**

| Property | Purpose |
| --- | --- |
| `HttpMethod` | Override inferred HTTP method (`EndpointHttpMethod.Get`, `.Post`, `.Put`, `.Delete`, `.Patch`) |
| `Route` | Custom route template (relative to group prefix; leading `/` for absolute) |
| `Name` | OpenAPI operation ID |
| `Summary` | Override XML doc summary for OpenAPI |
| `Description` | OpenAPI description |
| `Tags` | Override the group tags |
| `Exclude` | `true` to skip endpoint generation entirely |
| `EndpointFilters` | `IEndpointFilter` types for this endpoint |
| `SuccessStatusCode` | Override auto-detected success status code (200, 201, etc.) |
| `ProducesStatusCodes` | Explicit error status codes for OpenAPI (e.g., `[404, 400]`) |
| `Streaming` | `EndpointStreaming.ServerSentEvents` for SSE; `Default` for JSON array |
| `SseEventType` | SSE `event:` field name for `addEventListener` |

**Class-level defaults:** When applied to a class, settings apply to all methods unless a method-level attribute overrides them:

```csharp
[HandlerEndpoint(ProducesStatusCodes = [400, 500])]
public class ProductHandler
{
    // Inherits [400, 500] from class
    public Result<Product> Handle(CreateProduct command) { ... }

    // Overrides with its own set
    [HandlerEndpoint(ProducesStatusCodes = [404, 409])]
    public Result<Product> Handle(UpdateProduct command) { ... }
}
```

## HTTP Method Inference

The HTTP method is inferred from the message type name prefix:

| Message Name Pattern | HTTP Method |
| ------------------- | ----------- |
| `Get*`, `Find*`, `Search*`, `List*`, `Query*` | GET |
| `Create*`, `Add*`, `New*` | POST |
| `Update*`, `Edit*`, `Modify*`, `Set*`, `Change*` | PUT |
| `Delete*`, `Remove*` | DELETE |
| `Patch*` | PATCH |

**Action verbs** — prefixes like `Complete*`, `Approve*`, `Cancel*`, `Submit*`, `Archive*`, `Publish*`, etc., default to **POST** and produce an action route suffix:

```csharp
// POST /api/todos/{todoId}/complete
public Task<Result> HandleAsync(CompleteTodo command) { ... }

// POST /api/orders/{orderId}/cancel
public Task<Result> HandleAsync(CancelOrder command) { ... }
```

Any unrecognized prefix defaults to **POST**.

## Route Generation

The final route for every endpoint is built by concatenating up to three levels. Each level is **relative** to its parent by default — but any level that starts with `/` becomes **absolute** and discards everything above it.

| Level | Source | Default |
| ----- | ------ | ------- |
| 1. Global prefix | `[assembly: MediatorConfiguration(EndpointRoutePrefix = "...")]` | `"api"` |
| 2. Group prefix | `[HandlerEndpointGroup("Products")]` or auto-derived from class name | Class name minus `Handler`/`Consumer` suffix, lowercased |
| 3. Endpoint route | `[HandlerEndpoint(Route = "...")]` or auto-generated | ID properties as route params, action verb suffix |

```text
/api/products/{productId}
 ↑      ↑           ↑
 │      │           └─ 3. Endpoint route (auto-generated from GetProduct's ProductId property)
 │      └─ 2. Group prefix (from [HandlerEndpointGroup("Products")] or class name "ProductHandler")
 └─ 1. Global prefix (default "api")
```

### How Prefixes Concatenate

When all three levels are **relative** (no leading `/`), they concatenate left to right:

```text
Global("api")  +  Group("products")  +  Endpoint("{productId}")  →  /api/products/{productId}
Global("api")  +  Group("v2/items")  +  Endpoint("{itemId}")     →  /api/v2/items/{itemId}
Global("")     +  Group("products")  +  Endpoint("{productId}")  →  /products/{productId}
```

### Absolute Override with Leading `/`

A leading `/` on any prefix makes it **absolute** — it replaces everything above it in the hierarchy:

**Group-level override** — a `/` on the group prefix discards the global prefix:

```csharp
// Global prefix is "api", but this group bypasses it
[HandlerEndpointGroup("Health", RoutePrefix = "/health")]
public class HealthHandler
{
    public Result<Status> Handle(GetHealthCheck query) { ... }
}
// → GET /health  (not /api/health)
```

**Endpoint-level override** — a `/` on the endpoint route discards both the global and group prefix:

```csharp
[HandlerEndpointGroup("Products")]
public class ProductHandler
{
    [HandlerEndpoint(Route = "/status")]
    public Result<string> Handle(GetStatus query) { ... }
}
// → GET /status  (not /api/products/status)
```

**Summary:**

| Group `RoutePrefix` | Endpoint `Route` | Result (global = `"api"`) |
| --- | --- | --- |
| `"products"` (relative) | `"{productId}"` (relative) | `/api/products/{productId}` |
| `"/health"` (absolute) | `""` (relative) | `/health` |
| `"products"` (relative) | `"/status"` (absolute) | `/status` |
| `"/v2/products"` (absolute) | `"{productId}"` (relative) | `/v2/products/{productId}` |

### Route Pluralization

Entity names in convention-based routes are **automatically pluralized** to follow REST conventions. The entity name is extracted from the message name by removing the verb prefix (e.g., `GetProduct` → `Product`), then pluralized before being converted to kebab-case.

| Message Name | Entity | Pluralized Route |
| ------------ | ------ | ---------------- |
| `GetProduct` | Product | `/products/{productId}` |
| `GetProducts` | Products | `/products` (already plural) |
| `CreateTodo` | Todo | `/todos` |
| `GetCategory` | Category | `/categories/{categoryId}` |
| `GetPerson` | Person | `/people/{personId}` |
| `GetHealth` | Health | `/health` (uncountable) |

**Irregular nouns** are handled automatically: `Person` → `People`, `Child` → `Children`, `Index` → `Indices`, `Criterion` → `Criteria`, etc.

**Uncountable nouns** are not pluralized: `Health`, `Status`, `Data`, `Info`, `Auth`, `Config`, `Feedback`, `Metadata`, `Settings`, `Media`, `Cache`, `Analytics`, `Telemetry`, `Search`, `Content`, `Access`.

To override auto-pluralization, use an explicit route:

```csharp
[HandlerEndpoint(Route = "/custom-path/{id}")]
public Result<Item> Handle(GetItem query) { ... }
```

### Route Parameters

Properties named `Id` or ending with `Id` automatically become route parameters:

```csharp
public record GetProduct(string ProductId);
// → GET /api/products/{productId}
```

### Query Parameters

For GET/DELETE requests, non-ID properties become query parameters:

```csharp
public record SearchProducts(string? Category, int? MinPrice, int? MaxPrice);
// → GET /api/products?category=...&minPrice=...&maxPrice=...
```

### Avoiding Doubled Prefixes

A common mistake is repeating the global prefix inside the group prefix:

```text
Global("api")  +  Group("api/products")  →  /api/api/products  ⚠️ Wrong!
```

The group prefix `"api/products"` is relative, so it nests under the global `"api"` — producing a doubled path. Use `"products"` instead, or use an absolute path `"/api/products"` if you want to spell out the full path explicitly.

> The compiler emits warning **FMED015** if it detects a group `RoutePrefix` that duplicates the global prefix.

## Parameter Binding

### GET/DELETE Requests

**`[AsParameters]` binding** (message has a parameterless constructor):

```csharp
public record SearchProducts
{
    public string? Category { get; init; }
    public int Page { get; init; } = 1;
}
// → MapGet("/", async ([AsParameters] SearchProducts message, ...) => ...)
```

**Constructor binding** (message has required constructor parameters):

```csharp
public record GetProduct(string ProductId);
// → MapGet("/{productId}", async (string productId, ...) =>
//   { var message = new GetProduct(productId); ... })
```

### POST/PUT/PATCH Requests

Request body is bound using `[FromBody]`. For PUT/PATCH with route parameters, the body is merged with route values:

```csharp
public record UpdateProduct(string ProductId, string? Name, decimal? Price);
// → MapPut("/{productId}", async (string productId, [FromBody] UpdateProduct message, ...) =>
//   { var mergedMessage = message with { ProductId = productId }; ... })
```

## Result to HTTP Status Mapping

`Result<T>` and `Result` return values are automatically mapped to HTTP responses:

| ResultStatus | HTTP Status |
| ------------ | ----------- |
| `Success` | 200 OK |
| `Created` | 201 Created |
| `NoContent` | 204 No Content |
| `BadRequest` | 400 Bad Request |
| `Invalid` | 400 Bad Request (ValidationProblem) |
| `NotFound` | 404 Not Found |
| `Unauthorized` | 401 Unauthorized |
| `Forbidden` | 403 Forbidden |
| `Conflict` | 409 Conflict |
| `Error` | 500 Internal Server Error |
| `CriticalError` | 500 Internal Server Error |
| `Unavailable` | 503 Service Unavailable |

### File Downloads

`Result<FileResult>` automatically produces a file response:

```csharp
public class ReportHandler
{
    [HandlerEndpoint(HttpMethod = EndpointHttpMethod.Get, Route = "/reports/{id}")]
    public async Task<Result<FileResult>> HandleAsync(
        GetReport query, IReportService reports, CancellationToken ct)
    {
        var stream = await reports.GeneratePdfAsync(query.Id, ct);
        return Result.File(stream, "application/pdf", $"report-{query.Id}.pdf");
    }
}
```

### OpenAPI Error Responses

The generator scans your handler body for `Result` factory calls and emits matching `.ProducesProblem()` metadata automatically:

```csharp
public class OrderHandler
{
    public Result<OrderView> Handle(GetOrder query)
    {
        if (query.Id == null)
            return Result<OrderView>.NotFound("Order not found");  // → .ProducesProblem(404)

        if (!IsValid(query))
            return Result<OrderView>.Invalid("Bad request");       // → .ProducesProblem(400)

        return new OrderView(query.Id, "Test");
    }
    // Auto-generates: .Produces<OrderView>(200), .ProducesProblem(404), .ProducesProblem(400)
}
```

### Success Status Codes

If a handler body contains `Result.Created()`, the endpoint is generated with **201 Created**; otherwise it defaults to **200 OK**. Override with `[HandlerEndpoint(SuccessStatusCode = 201)]`.

## Authentication & Authorization

Authorization works for **both** HTTP endpoints and direct `mediator.InvokeAsync()` calls, configured via `[HandlerAuthorize]` and `[HandlerAllowAnonymous]`:

```csharp
// Require auth on a handler
[HandlerAuthorize(Roles = ["Admin", "Manager"])]
public class AdminHandler
{
    public Task<Result> HandleAsync(DeleteProduct command) { ... }
}

// Require auth globally
[assembly: MediatorConfiguration(AuthorizationRequired = true)]

// Opt out specific handlers
[HandlerAllowAnonymous]
public class PublicHandler
{
    public Task<Result<Status>> HandleAsync(HealthCheck query) { ... }
}
```

Authorization cascades: assembly defaults → group level → method level. For Result-returning handlers, unauthorized requests receive `Result.Unauthorized()` or `Result.Forbidden()`. For non-Result handlers, an `UnauthorizedAccessException` is thrown.

The authorization system is extensible via `IAuthorizationContextProvider` (provides the `ClaimsPrincipal`) and `IHandlerAuthorizationService` (performs the auth check). ASP.NET Core apps get automatic registration that reads from `HttpContext.User`.

## Discovery Modes

Control which handlers generate endpoints:

| Mode | Behavior |
| --- | --- |
| `EndpointDiscovery.All` (default) | All handlers get endpoints; use `[HandlerEndpoint(Exclude = true)]` to opt out |
| `EndpointDiscovery.Explicit` | Only handlers with `[HandlerEndpoint]` or `[HandlerEndpointGroup]` get endpoints |
| `EndpointDiscovery.None` | No endpoints generated |

```csharp
[assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.Explicit)]
```

## Events and Notifications

Handlers for event/notification types are automatically excluded from endpoint generation:

- Types implementing `INotification`, `IEvent`, `IDomainEvent`, or `IIntegrationEvent`
- Handler classes named `*EventHandler` or `*NotificationHandler`
- Types with names ending in event suffixes: `Created`, `Updated`, `Deleted`, `Changed`, `Removed`, `Added`, `Event`, `Notification`, `Published`, `Occurred`, `Happened`, `Started`, `Completed`, `Failed`, `Cancelled`, `Expired`

::: info INotification Is Not Required
Events are excluded based on naming conventions regardless of interface implementation. `INotification` is a classification tool — use it when you want a handler that can receive all notification-type messages, or simply as self-documentation.
:::

## OpenAPI / XML Documentation

To get endpoint summaries from your handler's XML doc comments, enable documentation generation:

```xml
<PropertyGroup>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
</PropertyGroup>
```

XML doc `<summary>` comments on handler methods automatically become the OpenAPI summary for the generated endpoint.

## Advanced Configuration

### Assembly Endpoint Options

```csharp
app.MapMediatorEndpoints(c =>
{
    c.AddAssembly<CreateProduct>();    // Products.Module
    c.AddAssembly<CreateOrder>();      // Orders.Module
    c.LogEndpoints();                  // Log all mapped routes at startup
});
```

### Global Settings

```csharp
[assembly: MediatorConfiguration(
    EndpointDiscovery = EndpointDiscovery.All,
    EndpointRoutePrefix = "api",          // Global route prefix (default: "api")
    AuthorizationRequired = false,         // Require auth for all endpoints
    EndpointFilters = [typeof(MyFilter)]  // Global endpoint filters
)]
```

Set `EndpointRoutePrefix = ""` to disable the global prefix entirely.

## Troubleshooting

### Endpoints Not Generated

1. Ensure your project references ASP.NET Core (has `Microsoft.AspNetCore.Routing`)
2. Check `[assembly: MediatorConfiguration(EndpointDiscovery = ...)]` — default is `All`. Make sure it hasn't been set to `None` or `Explicit`.
3. In `Explicit` mode, handlers need `[HandlerEndpoint]` or `[HandlerEndpointGroup]`
4. Verify the handler isn't excluded via `[HandlerEndpoint(Exclude = true)]`

### XML Summaries Not Appearing

1. Enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
2. Rebuild the project completely

### Route Conflicts

When multiple handlers generate the same route, the generator automatically differentiates them using the message type name in kebab-case.
