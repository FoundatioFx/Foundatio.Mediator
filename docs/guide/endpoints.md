# Endpoint Generation

::: tip Optional Feature
Endpoint generation is entirely **opt-in**. Foundatio Mediator works perfectly as a pure in-process mediator without any HTTP layer. If you don't call `.MapMediatorEndpoints()`, no endpoints are generated and no ASP.NET Core dependencies are required. Use this feature only when you want to expose your handlers as a REST API.
:::

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
    [HandlerEndpoint(HandlerMethod.Post, "{todoId}/complete")]
    public Task<Result> HandleAsync(CompleteTodo command) { ... }

    // Custom OpenAPI metadata
    [HandlerEndpoint(Name = "BulkCreateTodos", Summary = "Creates multiple todos at once")]
    public Task<Result<List<Todo>>> HandleAsync(BulkCreateTodos command) { ... }

    // Exclude from endpoint generation
    [HandlerEndpoint(Exclude = true)]
    public Task<Result> HandleAsync(InternalCleanup command) { ... }
}
```

**Constructor overloads:**

The attribute supports three constructor forms for convenience:

```csharp
[HandlerEndpoint]                                     // Fully convention-based
[HandlerEndpoint("/{productId}")]                     // Lock the route only
[HandlerEndpoint(HandlerMethod.Get, "/{productId}")]  // Lock both the HTTP method and route
```

All properties (including `Method` and `Route`) are also settable as named arguments for maximum flexibility:

```csharp
[HandlerEndpoint(Method = HandlerMethod.Post, Route = "{todoId}/complete")]
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
| `Method` | Override inferred HTTP method (`HandlerMethod.Get`, `.Post`, `.Put`, `.Delete`, `.Patch`) |
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

## Converting to Explicit Endpoints

By default, routes and HTTP methods are derived from conventions — message names, handler class names, and property types. This is great for rapid development, but renaming a message or refactoring a handler class can silently change your API surface.

Converting to **explicit endpoints** freezes the route and HTTP method into attributes so they won't change when you refactor.

### Using the Code Fix

The analyzer reports an `FMED017` info diagnostic on every handler method that generates (or could generate) an endpoint, showing the computed route:

```text
Endpoint: GET /api/products/{productId}
```

Click the lightbulb (or `Ctrl+.`) to see:

- **Make endpoint explicit** — adds `[HandlerEndpoint(HandlerMethod.Get, "/{productId}")]` to the method and `[HandlerEndpointGroup("Products")]` to the class (if not already present)
- **Make all endpoints in class explicit** — applies the same to every handler method in the class
- **Fix All** → Document / Project / Solution — converts endpoints across a wider scope

Once converted, the FMED017 diagnostic still shows the route but the code fix no longer appears (the endpoint is already explicit).

### Explicit Discovery Mode

When `EndpointDiscovery = EndpointDiscovery.Explicit`, only handlers with `[HandlerEndpoint]` generate endpoints. The FMED017 diagnostic still appears on all handler methods — showing what the route _would_ be — so you can use the code fix to opt in:

```text
Endpoint: GET /api/products/{productId} (not generated — add [HandlerEndpoint] to opt in)
```

Clicking "Make endpoint explicit" adds the attribute, which simultaneously opts the handler in and freezes the route.

### Converting Manually

You can also add the attributes manually using any of the constructor forms:

```csharp
[HandlerEndpoint(HandlerMethod.Get, "/{productId}")]   // Explicit verb and route
[HandlerEndpoint("/{productId}")]                       // Explicit route only (verb still inferred)
[HandlerEndpoint(Method = HandlerMethod.Get)]           // Explicit verb only (route still inferred)
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

When a handler is invoked through a generated endpoint, `HttpContext`, `HttpRequest`, `HttpResponse`, and `ClaimsPrincipal` are automatically available as handler method parameters — no DI registration required:

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
        HttpResponse response)     // Also works with HttpRequest / HttpResponse / ClaimsPrincipal directly
    {
        response.Headers.Append("X-Export-Id", Guid.NewGuid().ToString());
        // ...
    }

    public Result<string> Handle(
        GetCurrentUser query,
        ClaimsPrincipal user)      // ClaimsPrincipal from HttpContext.User
    {
        return user.Identity?.Name ?? "anonymous";
    }
}
```

The endpoint generator populates a `CallContext` with these values before calling the handler. When the same handler is invoked directly via `mediator.InvokeAsync()` (without an endpoint), these parameters fall back to normal DI resolution.

::: warning Avoid HTTP types in handlers when possible
Using `HttpContext`, `HttpRequest`, `HttpResponse`, or `ClaimsPrincipal` in a handler **couples it to HTTP** — the handler can only work when called from an endpoint, and it becomes harder to unit test (you need to mock or construct HTTP types). Prefer putting all the data you need into your **message type** instead:

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

### Custom Result Mapping

To customize how `Result` statuses are converted to HTTP responses, implement `IMediatorResultMapper<IResult>` and register it before `AddMediator()`:

```csharp
using Microsoft.AspNetCore.Http;

public class CustomResultMapper : IMediatorResultMapper<IResult>
{
    public IResult MapResult(Foundatio.Mediator.IResult result) => result.Status switch
    {
        // Use ProblemDetails for NotFound instead of the default anonymous object
        ResultStatus.NotFound => Results.Problem(
            detail: result.Message, statusCode: 404, title: "Not Found"),

        ResultStatus.BadRequest => Results.Problem(
            detail: result.Message, statusCode: 400, title: "Bad Request"),

        // Handle all other statuses
        _ => Results.Problem(result.Message ?? "An unexpected error occurred", statusCode: 500)
    };
}
```

```csharp
// Register before AddMediator — your implementation takes priority
services.AddSingleton<IMediatorResultMapper<IResult>, CustomResultMapper>();
services.AddMediator();
```

When no custom mapper is registered, the generated default handles all `ResultStatus` values with sensible HTTP status codes.

### File Downloads

`Result<FileResult>` automatically produces a file response:

```csharp
public class ReportHandler
{
    [HandlerEndpoint(HandlerMethod.Get, "/reports/{id}")]
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

## Endpoint Conventions

Endpoint conventions let you create reusable attributes that configure generated endpoints at startup — rate limiting, CORS, caching headers, or any other endpoint builder customization. Implement `IEndpointConvention<TBuilder>` on an attribute and the source generator handles the rest. No runtime reflection.

### Defining a Convention

Create an attribute that implements `IEndpointConvention<RouteHandlerBuilder>`:

```csharp
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RateLimitedAttribute : Attribute, IEndpointConvention<RouteHandlerBuilder>
{
    public string? PolicyName { get; }

    public RateLimitedAttribute(string? policyName = null) => PolicyName = policyName;

    public void Configure(RouteHandlerBuilder builder)
    {
        builder.RequireRateLimiting(PolicyName ?? "default");
    }
}
```

The `Configure` method receives the endpoint builder at startup and can call any builder extension method — `RequireRateLimiting`, `RequireCors`, `CacheOutput`, `WithMetadata`, etc.

### Applying Conventions

Conventions can be applied at three scopes:

```csharp
// Assembly level — applies to ALL endpoints in this assembly
[assembly: RateLimited]

// Class level — applies to all endpoints in this handler
[RateLimited("moderate")]
public class OrderHandler
{
    // Method level — applies to this endpoint only
    [RateLimited("strict")]
    public Task<Result<Order>> HandleAsync(CreateOrder cmd) { ... }

    // Inherits class-level "moderate" policy
    public Task<Result<Order>> HandleAsync(GetOrder query) { ... }
}
```

### Most-Derived Wins

When the same convention attribute appears at multiple scopes, the most specific one wins:

| Scope | Priority |
| --- | --- |
| **Method** | Highest — overrides class and assembly |
| **Class** | Overrides assembly |
| **Assembly** | Lowest — global default |

This lets you set a global default and override it where needed:

```csharp
// Global default: all endpoints get "default" rate limit
[assembly: RateLimited]

public class ProductHandler
{
    // Inherits assembly default → "default" policy
    public Result<List<Product>> Handle(GetProducts query) { ... }

    // Overrides with "strict" for write operations
    [RateLimited("strict")]
    public Task<Result<Product>> HandleAsync(CreateProduct cmd) { ... }
}
```

For each attribute type, only one scope applies per endpoint — there's no stacking. If you need multiple independent behaviors, use separate attribute types.

### Group Conventions

To configure the `RouteGroupBuilder` (shared prefix group) instead of individual endpoints, implement `IEndpointConvention<RouteGroupBuilder>`:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class GroupCorsAttribute : Attribute, IEndpointConvention<RouteGroupBuilder>
{
    public string PolicyName { get; } = "default";

    public GroupCorsAttribute(string? policyName = null) => PolicyName = policyName ?? "default";

    public void Configure(RouteGroupBuilder group)
    {
        group.RequireCors(PolicyName);
    }
}

[HandlerEndpointGroup("Orders")]
[GroupCors("allow-frontend")]
public class OrderHandler { ... }
```

Group conventions applied at the class level configure the group builder, affecting all endpoints in that group.

### How It Works

The source generator:

1. Detects attributes implementing `IEndpointConvention<T>` on methods, classes, and the assembly
2. Records their constructor arguments and named properties
3. Emits code that reconstructs each attribute and calls `Configure(builder)` at startup

No reflection is used at runtime. The generated code looks like:

```csharp
// Generated — you never write this
var ep = ordersGroup.MapPost("", async (...) => { ... });
((IEndpointConvention<RouteHandlerBuilder>)new RateLimitedAttribute("strict")).Configure(ep);
```

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
