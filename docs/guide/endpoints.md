# Endpoint Generation

Foundatio Mediator automatically generates ASP.NET Core Minimal API endpoints from your handlers. Write your handlers as plain message-in/result-out methods, call `.MapMediatorEndpoints()`, and you have a fully functional API — with smart route conventions, HTTP method inference, and OpenAPI metadata — all without writing a single controller or endpoint definition.

Because your handler logic is completely decoupled from HTTP, it's trivially testable: just call the handler directly and assert the result. The endpoint layer is a thin, generated projection that you never maintain by hand.

## Quick Start

Write a handler:

```csharp
public class ProductHandler
{
    /// <summary>Create a new product</summary>
    public Task<Result<Product>> HandleAsync(CreateProduct command) { ... }
    /// <summary>Get a product by ID</summary>
    public Result<Product> Handle(GetProduct query) { ... }
    /// <summary>List all products</summary>
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
- **OpenAPI metadata** — operation names, status codes, and even error responses are auto-detected from your `Result` factory calls. XML `<summary>` comments on handler methods automatically become the OpenAPI summary for each endpoint.
- **Result mapping** — `Result<T>` return values are automatically converted to the correct HTTP status codes

### Why This Matters

This architecture gives you a **loosely coupled, message-oriented application** with close to zero boilerplate. Your handlers don't know they're behind HTTP — they receive a message and return a result. This means:

- **Testing is trivial** — handlers are plain methods with no framework code, so you can call them directly in a unit test. No mediator, no `HttpClient`, no test server, no request serialization.
- **Transport-agnostic** — the same handler works through HTTP endpoints, direct mediator calls, background jobs, or SignalR — the handler doesn't care.
- **Always in sync** — endpoints are generated from your handler code, so your API can never drift from your business logic.

## Streaming Endpoints & Server-Sent Events

Handlers that return `IAsyncEnumerable<T>` automatically become streaming HTTP endpoints. Combined with `SubscribeAsync`, you can push real-time domain events to the browser in just a few lines:

```csharp
public record GetEventStream;
public record ClientEvent(string EventType, object Data);

public class EventStreamHandler(IMediator mediator)
{
    [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
    public async IAsyncEnumerable<ClientEvent> Handle(
        GetEventStream message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in mediator.SubscribeAsync<INotification>(
            cancellationToken))
        {
            yield return new ClientEvent(evt.GetType().Name, evt);
        }
    }
}
```

That generates `GET /api/events` as an SSE endpoint. Any browser client can subscribe:

```javascript
const source = new EventSource('/api/events');
source.onmessage = (e) => {
    const event = JSON.parse(e.data);
    console.log(event.eventType, event.data);
};
```

Whenever any handler publishes a notification, every connected SSE client receives it instantly. Zero polling, zero WebSocket infrastructure.

### JSON Array Streaming

Without the SSE attribute, streaming handlers return a JSON array streamed incrementally — useful for large datasets that you don't want to buffer in memory:

```csharp
public class SalesHandler
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

This generates a `GET /api/sales` endpoint that streams results as they're produced — ASP.NET Core sends each item to the client without waiting for the full result set.

| `Streaming` Value | Behavior |
| --- | --- |
| `EndpointStreaming.Default` | JSON array streaming (default for `IAsyncEnumerable<T>` handlers) |
| `EndpointStreaming.ServerSentEvents` | SSE via `TypedResults.ServerSentEvents()` (.NET 10+) |

| `SseEventType` | Behavior |
| --- | --- |
| `null` (default) | Browser `EventSource` fires the default `message` event |
| `"event"` | Clients listen with `addEventListener('event', ...)` |

::: tip
For `SubscribeAsync`, dynamic subscriptions, and SSE details, see [Streaming Handlers](./streaming-handlers.md).
:::

## Customization Attributes

Everything works out of the box with smart defaults. Attributes are only needed when you want to change a default behavior — group endpoints with a shared route prefix, override a route, change an HTTP method, or exclude a handler from generation.

### `[HandlerEndpointGroup]` — Group Endpoints

`[HandlerEndpointGroup]` is applied to a handler **class** and controls all endpoints in that class as a group. Use it to set a shared route prefix, OpenAPI tag, or endpoint filters for every handler method on the class.

The group name is optional — when omitted, it's derived from the class name (e.g., `ProductHandler` → `Products`):

```csharp
[HandlerEndpointGroup(RoutePrefix = "v2/products")]
public class ProductHandler
{
    public Task<Result<Product>> HandleAsync(CreateProduct command) { ... }
    public Result<Product> Handle(GetProduct query) { ... }
}
```

This overrides the default route prefix (which would be `products` from the class name) with a versioned path:

```text
POST  /api/v2/products              → CreateProduct
GET   /api/v2/products/{productId}  → GetProduct
```

**When do you need `[HandlerEndpointGroup]`?**

- To set a **custom route prefix** different from the class name: `[HandlerEndpointGroup(RoutePrefix = "v2/products")]`
- To set a **custom OpenAPI tag** different from the class name: `[HandlerEndpointGroup(Name = "Inventory")]`
- To apply **endpoint filters** to all endpoints in the class: `[HandlerEndpointGroup("Orders", EndpointFilters = [typeof(AuditFilter)])]`
- To share auth, filter, or routing configuration across all handler methods in one place

**Properties:**

| Property | Purpose |
| --- | --- |
| `Name` | Group name used as the OpenAPI tag and default route prefix (kebab-cased). When omitted, auto-derived from the class name (e.g., `OrderHandler` → `"Orders"`) |
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

## How Routes Are Generated

The generator builds every endpoint route through a simple four-step algorithm. Understanding these steps lets you predict exactly what route any message name will produce — and tells you when to reach for an explicit attribute instead.

### Step 1: Determine the Mode

Every handler class operates in one of two modes, chosen automatically:

| Mode | When it activates | Where the entity comes from |
| --- | --- | --- |
| **Single-message** | Class has exactly **1** handler method **and** the class name matches the message name | The message name |
| **Group** | Class has **2+** methods, **or** the class name doesn't match the message | The handler class name |

"Class name matches the message name" means the class name minus the `Handler`/`Consumer` suffix equals the message type name. For example, `GetOrderHandler` matches `GetOrder`, but `OrderHandler` does not.

```csharp
// Single-message mode: "GetOrder" == "GetOrder" + Handler
public class GetOrderHandler
{
    public Result<Order> Handle(GetOrder query) { ... }
}

// Group mode: "Order" ≠ "GetOrder" (and has 3 methods)
public class OrderHandler
{
    public Result<Order> Handle(GetOrder query) { ... }
    public Result<Order> Handle(CreateOrder cmd) { ... }
    public Result Handle(CompleteOrder cmd) { ... }
}
```

**Why two modes?** Single-message mode keeps things minimal — one handler, one route, no endpoint group or OpenAPI tag. Group mode creates an endpoint group from the class name so all methods in the class share a common route prefix and OpenAPI tag.

### Step 2: Determine the Entity

The **entity** is the REST resource your handler operates on. It determines the base path of the route.

**In group mode**, the entity comes from the handler class name:

| Handler Class | Entity | Route Prefix |
| --- | --- | --- |
| `OrderHandler` | Order | `/orders` |
| `ShoppingCartHandler` | ShoppingCart | `/shopping-carts` |
| `TodoHandler` | Todo | `/todos` |

The class name is stripped of its `Handler`/`Consumer` suffix, pluralized, and kebab-cased. This becomes the group route prefix — every method in the class inherits it.

**In single-message mode**, there's no group prefix. The entity is extracted from the message name (after stripping the verb — see step 3) and becomes the route directly:

| Message | Verb | Entity | Route |
| --- | --- | --- | --- |
| `GetOrder` | Get | Order | `GET /api/orders/{orderId}` |
| `CreateTodo` | Create | Todo | `POST /api/todos` |
| `CompleteTodo` | Complete | Todo | `POST /api/todos/{todoId}/complete` |

### Step 3: Infer the HTTP Method

The first word of the message name (split at the PascalCase boundary) determines the HTTP method:

| Prefix | HTTP Method |
| --- | --- |
| `Get`, `Find`, `Search`, `List`, `Query` | **GET** |
| `Create`, `Add`, `New` | **POST** |
| `Update`, `Edit`, `Modify`, `Change`, `Set` | **PUT** |
| `Delete`, `Remove` | **DELETE** |
| `Patch` | **PATCH** |
| _Anything else_ | **POST** |

These CRUD prefixes are the only special-cased verbs. Everything else — `Complete`, `Approve`, `Cancel`, `Export`, `Finalize`, `Validate`, literally _any_ verb — defaults to POST and becomes a route action suffix (see step 4).

### Step 4: Build the Route

With the mode, entity, and HTTP method determined, the generator strips the entity from the message name. Whatever's left becomes the route.

**CRUD verbs** are stripped cleanly — they map to HTTP methods and don't produce a route suffix:

```text
GetOrder     → strip "Get"      → entity "Order"   → GET /orders/{orderId}
CreateOrder  → strip "Create"   → entity "Order"   → POST /orders
DeleteOrder  → strip "Delete"   → entity "Order"   → DELETE /orders/{orderId}
```

**Action verbs** — anything that's not a CRUD prefix — are split at the first PascalCase word boundary. The first word is the action, the rest is the entity:

```text
CompleteTodo  → "Complete" + "Todo"  → POST /todos/{todoId}/complete
ArchiveOrder  → "Archive"  + "Order" → POST /orders/{orderId}/archive
ExportOrders  → "Export"   + "Orders"→ POST /orders/export
FinalizeOrder → "Finalize" + "Order" → POST /orders/{orderId}/finalize
```

The action verb is kebab-cased and appended as a route suffix. No hardcoded list of action verbs is needed — the generator figures it out by splitting PascalCase.

**Single-word messages** (no PascalCase boundary, like `Login`, `Logout`, `Ping`) are treated as bare actions. In group mode, they become a route segment under the group prefix:

```csharp
public class AuthHandler
{
    public Result Handle(Login cmd) { ... }    // → POST /api/auth/login
    public Result Handle(Logout cmd) { ... }   // → POST /api/auth/logout
}
```

### Putting It All Together

Here's the full algorithm applied to a typical handler:

```csharp
public class OrderHandler
{
    public Result<Order> Handle(GetOrder query) { ... }
    public Result<Order[]> Handle(GetOrders query) { ... }
    public Result<Order> Handle(CreateOrder cmd) { ... }
    public Result<Order> Handle(UpdateOrder cmd) { ... }
    public Result Handle(DeleteOrder cmd) { ... }
    public Result Handle(CompleteOrder cmd) { ... }
    public Result Handle(ExportOrders cmd) { ... }
}
```

| Message | Step 1: Mode | Step 2: Entity | Step 3: HTTP | Step 4: Route |
| --- | --- | --- | --- | --- |
| `GetOrder` | Group (multi-method) | "Order" from class | `Get` → GET | Strip `Get`+`Order` → nothing left → `GET /api/orders/{orderId}` |
| `GetOrders` | Group | "Order" from class | `Get` → GET | Strip `Get`+`Orders` → nothing left → `GET /api/orders` |
| `CreateOrder` | Group | "Order" from class | `Create` → POST | Strip `Create`+`Order` → nothing left → `POST /api/orders` |
| `UpdateOrder` | Group | "Order" from class | `Update` → PUT | Strip `Update`+`Order` → nothing left → `PUT /api/orders/{orderId}` |
| `DeleteOrder` | Group | "Order" from class | `Delete` → DELETE | Strip `Delete`+`Order` → nothing left → `DELETE /api/orders/{orderId}` |
| `CompleteOrder` | Group | "Order" from class | `Complete` → POST | Strip `Order` → "Complete" left → `POST /api/orders/{orderId}/complete` |
| `ExportOrders` | Group | "Order" from class | `Export` → POST | Strip `Orders` → "Export" left → `POST /api/orders/export` |

And the same entity split across separate single-message handlers:

```csharp
public class GetOrderHandler { public Result<Order> Handle(GetOrder q) { ... } }
public class CreateOrderHandler { public Result<Order> Handle(CreateOrder c) { ... } }
public class CompleteOrderHandler { public Result Handle(CompleteOrder c) { ... } }
```

| Message | Step 1: Mode | Route |
| --- | --- | --- |
| `GetOrder` | Single (class matches) | `GET /api/orders/{orderId}` |
| `CreateOrder` | Single (class matches) | `POST /api/orders` |
| `CompleteOrder` | Single (class matches) | `POST /api/orders/{orderId}/complete` |

Both approaches produce identical routes — organize your handlers however you prefer.

### Route Structure

The final route is built by joining up to three levels:

| Level | Source | Default |
| --- | --- | --- |
| 1. Global prefix | `[assembly: MediatorConfiguration(EndpointRoutePrefix = "...")]` | `"api"` |
| 2. Group prefix | `[HandlerEndpointGroup("Name")]` or auto-derived from handler class | Pluralized entity, kebab-cased |
| 3. Endpoint route | `[HandlerEndpoint(Route = "...")]` or auto-generated | Route params + action suffix |

```text
/api/orders/{orderId}/complete
 ↑      ↑       ↑        ↑
 │      │       │        └─ Action suffix (from "Complete" in CompleteOrder)
 │      │       └─ Route parameter (from OrderId property)
 │      └─ Group prefix (from OrderHandler → "orders")
 └─ Global prefix (default "api")
```

::: tip
Routes are automatically **pluralized**: `TodoHandler` → `/todos`, `CategoryHandler` → `/categories`, `PersonHandler` → `/people`. Irregular nouns are handled automatically.

**Uncountable nouns** are not pluralized: `Health`, `Status`, `Data`, `Auth`, `Config`, `Settings`, `Media`, `Cache`, `Analytics`, etc.
:::

### Entity Name Normalization

The generator strips common CQRS qualifiers from message names before deriving routes. This keeps routes clean regardless of your naming style:

| Pattern | Example | What's stripped | Route |
| ------- | ------- | --------------- | ----- |
| `All` prefix | `GetAllTodos` | `All` | `GET /api/todos` |
| `ById` suffix | `GetTodoById` | `ById` | `GET /api/todos/{id}` |
| `Details` / `Detail` | `GetOrderDetails` | `Details` | `GET /api/orders/{id}` |
| `Summary` | `GetOrderSummary` | `Summary` | `GET /api/orders/{id}` |
| `Paged` / `Paginated` | `GetProductsPaged` | `Paged` | `GET /api/products` |
| `List` / `Stream` | `GetTodoList` | `List` | `GET /api/todos` |
| `With<Feature>` | `GetTodosWithPagination` | `WithPagination` | `GET /api/todos` |

Some patterns produce **route segments** instead of being stripped:

| Pattern | Example | Route |
| ------- | ------- | ----- |
| `Count` suffix | `GetOrderCount` | `GET /api/orders/count` |
| `By<Property>` | `GetOrderByEmail` | `GET /api/orders/by-email` |
| `For<Entity>` | `GetOrdersForCustomer` | `GET /api/orders/for-customer/{customerId}` |
| `From<Entity>` | `GetShipmentsFromWarehouse` | `GET /api/shipments/from-warehouse/{warehouseId}` |

::: tip
`By<Property>`, `For<Entity>`, and `From<Entity>` produce route segments under the entity, keeping all routes grouped. For example, `GetTodoByName(string Name)` generates `GET /api/todos/by-name?name=...` — no conflict with the list route `GET /api/todos`.
:::

### Sub-Entity Routes

When a message entity doesn't match the handler's group entity, it appears as a sub-route:

```csharp
public class TodoHandler
{
    public Result<Todo> Handle(GetTodo query) { ... }              // → GET /api/todos/{todoId}
    public Result<Item[]> Handle(GetTodoItems query) { ... }       // → GET /api/todos/items
    public Result<User> Handle(GetCurrentUser query) { ... }       // → GET /api/todos/current-user
}
```

`TodoItems` starts with the group entity `Todo`, so it's recognized as a sub-entity — only the `Items` part becomes a route segment. `CurrentUser` doesn't match the group entity at all, so it appears as a singular sub-resource.

### Absolute Routes

A leading `/` on any level makes it **absolute** — it discards everything above it:

```csharp
// Leading / on group prefix → bypasses global "api" prefix
[HandlerEndpointGroup("Health", RoutePrefix = "/health")]
public class HealthHandler { ... }
// → GET /health  (not /api/health)

// Leading / on endpoint route → bypasses both global and group prefix
[HandlerEndpoint(Route = "/status")]
public Result<string> Handle(GetStatus query) { ... }
// → GET /status  (not /api/products/status)
```

| Group `RoutePrefix` | Endpoint `Route` | Result (global = `"api"`) |
| --- | --- | --- |
| `"products"` (relative) | `"{productId}"` (relative) | `/api/products/{productId}` |
| `"/health"` (absolute) | `""` (relative) | `/health` |
| `"products"` (relative) | `"/status"` (absolute) | `/status` |
| `"/v2/products"` (absolute) | `"{productId}"` (relative) | `/v2/products/{productId}` |

::: warning Route Stability
Convention-based routes may evolve as naming heuristics improve across library versions. If your API routes are part of a **public contract** (consumed by external clients, documented in an OpenAPI spec, or pinned in integration tests), use explicit attributes to guarantee stability:

```csharp
[HandlerEndpointGroup(RoutePrefix = "orders")]
public class OrderHandler
{
    [HandlerEndpoint(Route = "{orderId}")]
    public Result<Order> Handle(GetOrder query) { ... }

    [HandlerEndpoint(Route = "{orderId}/promote")]
    public Result Handle(PromoteOrder cmd) { ... }
}
```

For internal APIs or rapid prototyping, convention-based routes are ideal.
:::

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

### Binding Attributes on Message Properties

You can use `[FromHeader]`, `[FromQuery]`, and `[FromRoute]` on message properties to control how individual values are bound in endpoints. This is useful for values like tenant IDs, correlation IDs, or API keys that come from HTTP headers rather than the request body.

**POST with a header-bound property:**

```csharp
public record CreateOrder(
    string CustomerId,
    decimal Amount,
    [property: FromHeader(Name = "X-Tenant-Id")] string TenantId
);
```

The generator extracts the attributed property as a separate endpoint parameter and merges it back into the message:

```csharp
// Generated:
MapPost("/orders", ([FromBody] CreateOrder message,
    [FromHeader(Name = "X-Tenant-Id")] string tenantId,
    HttpContext httpContext, IMediator mediator, CancellationToken ct) =>
{
    var mergedMessage = message with { TenantId = tenantId };
    // ...
})
```

**GET with a header-bound property:**

For GET/DELETE messages with a parameterless constructor, `[AsParameters]` binding handles header attributes natively — no extra work from the generator:

```csharp
public record SearchProducts
{
    public string? Category { get; init; }

    [FromHeader(Name = "X-Tenant-Id")]
    public string TenantId { get; init; } = "";
}
// → MapGet("/", ([AsParameters] SearchProducts message, ...) => ...)
// ASP.NET binds Category from query string, TenantId from header
```

::: tip Record syntax
C# records use positional parameters that default to **constructor** attribute targets. To place an attribute on the generated **property**, use the `property:` target prefix:

```csharp
public record MyMessage(
    string Name,
    [property: FromHeader(Name = "X-Custom")] string CustomValue
);
```

:::

::: info Transport-agnostic
The message is still the contract. When calling via `mediator.InvokeAsync()`, you provide all values directly — the binding attributes are only used by the endpoint generator:

```csharp
// From an endpoint: TenantId comes from the X-Tenant-Id header automatically
// From code: you provide it explicitly
await mediator.InvokeAsync(new CreateOrder("cust-1", 99.99m, "tenant-42"));
```

:::

### Accessing HTTP Types in Handlers

When a handler is invoked through a generated endpoint, `HttpContext`, `HttpRequest`, and `HttpResponse` are automatically available as handler method parameters — no DI registration required:

```csharp
public class ProductHandler
{
    public Result<Product> Handle(
        GetProduct query,
        HttpContext httpContext)   // Auto-resolved from the endpoint
    {
        var userAgent = httpContext.Request.Headers.UserAgent;
        // ...
    }

    public Result Handle(
        ExportProducts query,
        HttpResponse response)     // Also works with HttpRequest / HttpResponse directly
    {
        response.Headers.Append("X-Export-Id", Guid.NewGuid().ToString());
        // ...
    }
}
```

The endpoint generator populates a `CallContext` with these values before calling the handler. When the same handler is invoked directly via `mediator.InvokeAsync()` (without an endpoint), these parameters fall back to normal DI resolution.

::: warning Avoid HTTP types in handlers when possible
Using `HttpContext`, `HttpRequest`, or `HttpResponse` in a handler **couples it to HTTP** — the handler can only work when called from an endpoint, and it becomes harder to unit test (you need to mock or construct HTTP types). Prefer putting all the data you need into your **message type** instead:

```csharp
// ❌ Coupled to HTTP — hard to test, only works from endpoints
public Result<Product> Handle(GetProduct query, HttpContext httpContext)
{
    var tenant = httpContext.Request.Headers["X-Tenant-Id"].ToString();
    // ...
}

// ✅ Transport-agnostic — easy to test, works from anywhere
public record GetProduct(string Id, string TenantId);

public Result<Product> Handle(GetProduct query)
{
    // query.TenantId is populated by middleware or the endpoint binding
}
```

Reserve HTTP type parameters for edge cases where you genuinely need low-level HTTP access (e.g., streaming a response body, reading raw headers that can't be modeled as message properties).
:::

## Result to HTTP Status Mapping

`Result<T>` and `Result` return values are automatically mapped to HTTP responses:

| ResultStatus | HTTP Status |
| ------------ | ----------- |
| `Ok` | 200 OK |
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

// Opt out specific handlers — either attribute works
[HandlerAllowAnonymous]  // or [AllowAnonymous] from Microsoft.AspNetCore.Authorization
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

XML doc `<summary>` comments on handler methods automatically become the OpenAPI summary for the generated endpoint. Enable documentation generation in your project file:

```xml
<PropertyGroup>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
</PropertyGroup>
```

Then add `<summary>` comments to your handler methods:

```csharp
public class ProductHandler
{
    /// <summary>Create a new product in the catalog</summary>
    public Task<Result<Product>> HandleAsync(CreateProduct command) { ... }

    /// <summary>Get a product by its unique identifier</summary>
    public Result<Product> Handle(GetProduct query) { ... }

    /// <summary>List all products with optional filtering</summary>
    public Result<List<Product>> Handle(GetProducts query) { ... }
}
```

The generated endpoints will include these summaries in their OpenAPI metadata — visible in Swagger UI, Scalar, and any other OpenAPI tooling. You can also override the summary per-endpoint using `[HandlerEndpoint(Summary = "...")]`.

## API Versioning

Foundatio Mediator supports Stripe-style header-based API versioning. Routes stay the same across all versions — the client sends an `Api-Version` header to select which version they want. When no header is sent, the latest declared version is used by default.

### Declaring Versions

Declare your API versions at the assembly level:

```csharp
[assembly: MediatorConfiguration(
    ApiVersions = ["1", "2"],           // All declared versions
    ApiVersionHeader = "Api-Version"    // Header name (default)
)]
```

When `ApiVersions` is not set, no versioning logic is generated — everything works exactly as before (backward-compatible).

### Basic Usage — Unversioned Handlers

Most handlers need zero versioning boilerplate. Handlers without `ApiVersion` serve all versions automatically:

```csharp
[HandlerEndpointGroup("Products")]
public class ProductHandler
{
    // Available in ALL versions — no annotation needed
    public Result<Product> Handle(GetProduct query) { ... }
    public Result<Product> Handle(CreateProduct command) { ... }
}
```

### Version-Specific Handlers

When a breaking change is needed, create a separate handler class with an explicit `ApiVersion`. It overrides the default handler for that version on the same route:

```csharp
[HandlerEndpointGroup("Products", ApiVersion = "2")]
public class ProductV2Handler
{
    // Overrides GetProduct for version 2 only — returns a different DTO
    public Result<ProductDto> Handle(GetProduct query) { ... }
}
```

The generator detects that both `ProductHandler` and `ProductV2Handler` handle `GetProduct` on the same route, and emits a single endpoint with header-based dispatch:

```text
GET /api/products/{productId}
  → Api-Version: 1  → ProductHandler.Handle(GetProduct)
  → Api-Version: 2  → ProductV2Handler.Handle(GetProduct)  (default, latest)
  → No header       → ProductV2Handler.Handle(GetProduct)  (defaults to latest)
```

Non-overridden endpoints (like `CreateProduct`) are served by the unversioned handler regardless of the version header.

### Method-Level Version Override

Override the group version on individual methods:

```csharp
[HandlerEndpointGroup("Widgets", ApiVersion = "1")]
public class WidgetHandler
{
    // Inherits v1 from the group
    public string Handle(GetWidgetV1 query) => "v1";

    // Override to v2 for this method only
    [HandlerEndpoint(ApiVersion = "2")]
    public string Handle(GetWidgetV2 query) => "v2";
}
```

### Multi-Version Handlers

Expose a handler in specific versions without creating separate classes:

```csharp
[HandlerEndpointGroup("Products", ApiVersions = ["1", "2"])]
public class ProductHandler
{
    public Result<Product> Handle(GetProduct query) { ... }
}
```

### Deprecating Versions

Mark a version as deprecated to signal consumers it will be removed:

```csharp
[HandlerEndpointGroup("Products", ApiVersion = "1", Deprecated = true)]
public class ProductHandlerV1
{
    public Result<Product> Handle(GetProduct query) { ... }
}
```

Deprecated endpoints emit `[Obsolete]` metadata in the generated OpenAPI specification.

### Custom Version Header

Change the header name used for version selection:

```csharp
[assembly: MediatorConfiguration(
    ApiVersions = ["2024-01-15", "2025-03-01"],
    ApiVersionHeader = "X-Api-Version"    // Custom header name
)]
```

### Mixed Versioned and Unversioned Endpoints

Handlers without `ApiVersion` serve all versions automatically. Versioned handlers override specific routes for their version. Both coexist naturally:

```csharp
// No version — serves all versions at /api/health
public class HealthHandler
{
    public string Handle(GetHealth query) => "ok";
}

// Serves all versions at /api/products (default)
[HandlerEndpointGroup("Products")]
public class ProductHandler { ... }

// Overrides specific routes for version 2 at /api/products
[HandlerEndpointGroup("Products", ApiVersion = "2")]
public class ProductV2Handler { ... }
```

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
