# Foundatio.Mediator — Video Demo Script

> **Total runtime target:** 20–25 minutes
> **Primary demo app:** Clean Architecture Sample (modular monolith)
> **Prerequisites:** .NET 10 SDK, Node.js 20+, Docker running
> **Demo users:** `admin`/`admin` (Admin role), `user`/`user` (User role)

---

## Pre-Demo Setup

**Before recording, run the app so startup time doesn't eat into the video:**

```bash
cd samples/CleanArchitectureSample/src/AppHost
dotnet run
```

Wait for Aspire Dashboard to show all resources healthy:
- 3 API replicas (green)
- 3 Worker replicas (green)
- LocalStack container (SQS/SNS)
- Redis container
- Vite frontend

**Have these windows ready:**
1. Browser: SvelteKit frontend at `https://localhost:5199`
2. Browser: Aspire Dashboard (URL from terminal output)
3. IDE: VS Code with the sample open at `samples/CleanArchitectureSample/`
4. Terminal: For running curl commands (optional, the UI covers most)

---

## Part 1: Introduction & Hook (2 min)

### Script

> "Foundatio.Mediator is a high-performance mediator library for .NET that uses source generators and C# interceptors to achieve near-direct-call performance — with zero runtime reflection.
>
> Today I'm going to walk through a complete modular monolith application that demonstrates everything: convention-based handler discovery, auto-generated API endpoints, a rich middleware pipeline, real-time streaming, and — the new part — distributed queues and notifications powered by SQS and SNS.
>
> Let me start with the numbers."

### Show: Benchmark Results

Open `BenchmarkDotNet.Artifacts/results/Foundatio.Mediator.Benchmarks.CoreBenchmarks-report-github.md` or show this table:

| Scenario | Foundatio | MediatR | MassTransit | Wolverine |
|----------|-----------|---------|-------------|-----------|
| **Command** | **3.5 ns** | 37 ns | 1,376 ns | 179 ns |
| **Query** | **27 ns** | 61 ns | 5,015 ns | 253 ns |
| **Publish** | **16 ns** | 96 ns | 2,098 ns | 1,889 ns |
| **Cascading** | **151 ns** | 215 ns | 12,769 ns | 3,106 ns |

> "Commands run at 3.5 nanoseconds — that's essentially the same as a direct method call. Queries at 27 nanoseconds. Publishing an event at 16 nanoseconds. This is possible because everything is resolved at compile time — the source generator emits direct dispatch code, and C# interceptors redirect your `mediator.InvokeAsync()` calls to those generated methods. No dictionary lookups, no reflection, no allocations on the hot path."

---

## Part 2: Project Structure Overview (2 min)

### Show: Solution Explorer

Navigate through the project structure in VS Code:

```
samples/CleanArchitectureSample/src/
├── Common.Module/       → Cross-cutting: middleware, events, shared handlers
├── Orders.Module/       → Order processing bounded context
├── Products.Module/     → Product catalog bounded context
├── Reports.Module/      → Cross-module aggregation
├── Api/                 → ASP.NET Core composition root
├── AppHost/             → Aspire orchestrator
└── Web/                 → SvelteKit frontend
```

### Script

> "This is a modular monolith — four independent domain modules that communicate exclusively through the mediator. No module directly references another's handlers or data layer. The Reports module queries Orders and Products, but only through message types — it has no idea how those modules store their data.
>
> The Api project is the composition root. It wires up all the modules, configures distributed messaging, and calls `MapMediatorEndpoints()` to auto-generate all the API routes. The AppHost uses .NET Aspire to orchestrate 3 API replicas, 3 worker replicas, LocalStack for SQS/SNS, and Redis — all running locally."

### Show: `Api/Program.cs`

Open [samples/CleanArchitectureSample/src/Api/Program.cs](samples/CleanArchitectureSample/src/Api/Program.cs) and highlight:

```csharp
// Three lines to wire everything up
builder.Services.AddMediator()
    .AddDistributedQueues(opts => { opts.WorkersEnabled = options.IsWorkerEnabled; })
    .AddDistributedNotifications()
    .UseAws(aws => aws.ServiceUrl = builder.Configuration["AWS:ServiceURL"]!)
    .UseRedisJobState();

// Register your modules
builder.Services.AddCommonModule();
builder.Services.AddOrdersModule();
builder.Services.AddProductsModule();
builder.Services.AddReportsModule();

// One line generates all API endpoints
app.MapMediatorEndpoints();
```

> "That's it. `AddMediator()` discovers all handlers by naming convention. `AddDistributedQueues()` and `AddDistributedNotifications()` add the infrastructure. `MapMediatorEndpoints()` generates minimal API endpoints from every handler. Zero manual route registration."

---

## Part 3: Handlers & Convention-Based Discovery (3 min)

### Show: `Orders.Module/Handlers/OrderHandler.cs`

Open [samples/CleanArchitectureSample/src/Orders.Module/Handlers/OrderHandler.cs](samples/CleanArchitectureSample/src/Orders.Module/Handlers/OrderHandler.cs)

### Script

> "Here's the Order handler. Notice there's no interface to implement, no base class to inherit. The class is named `OrderHandler` — the suffix `Handler` is enough for the source generator to discover it at compile time.
>
> Each method takes a message as its first parameter. The generator matches message types to methods automatically. Additional parameters like `IOrderRepository` and `CancellationToken` are resolved from DI — method-level injection, not just constructor injection."

### Highlight: CreateOrder method

```csharp
[Retry]
[HandlerAuthorize(Roles = ["User", "Admin"])]
public async Task<(Result<Order>, OrderCreated?)> HandleAsync(
    CreateOrder command,
    IOrderRepository repository,
    CancellationToken cancellationToken)
```

> "Look at that return type — it's a tuple. The first element is the `Result<Order>` that goes back to the caller.  The second is `OrderCreated?` — a cascading event that's automatically published after the handler completes. The question mark means it's optional: return `null` and it simply isn't published. The handler doesn't know or care who will react to `OrderCreated`. That's the beauty — complete decoupling."

### Show: Products.Module UpdateProduct (multiple cascading events)

Open [samples/CleanArchitectureSample/src/Products.Module/Handlers/ProductHandler.cs](samples/CleanArchitectureSample/src/Products.Module/Handlers/ProductHandler.cs) and find `UpdateProduct`:

```csharp
public async Task<(Result<Product>, ProductUpdated?, ProductStockChanged?)> HandleAsync(
    UpdateProduct command, ...)
```

> "Products takes it further — `UpdateProduct` returns *two* optional cascading events. `ProductStockChanged` is only published when the stock quantity actually changes. Null events are simply not published. This gives you precise, conditional event publishing with zero ceremony."

---

## Part 4: Live Demo — Create & Observe Events (3 min)

### Action: Open the Frontend

Navigate to `https://localhost:5199` — the Dashboard page.

> "Let's see this in action. I've got the SvelteKit frontend running. The dashboard shows aggregate stats — total orders, total products, total revenue — all fetched through the Reports module, which queries Orders and Products via the mediator."

### Action: Open the Events Page

Click **Events** in the navigation. Show the connection status indicator (green dot).

> "This page shows a real-time event stream using Server-Sent Events. It's powered by a streaming handler that returns `IAsyncEnumerable<ClientEvent>` — the mediator's `SubscribeAsync` API yields every event as it's published anywhere in the system."

### Action: Log in as Admin

Go to **Login**, enter `admin`/`admin`.

### Action: Create a Product

Go to **Products** → click **Create** → fill in:
- Name: "Wireless Keyboard"
- Description: "Bluetooth mechanical keyboard"
- Price: 79.99
- Stock: 100

Submit.

### Observe

> "Watch the Events page — " *(switch to it or have it open in a split)*

Events should appear:
- `ProductCreated` (green badge)

> "One form submission, and the event was published to *all* replicas via SNS. The audit handler logged it. The notification handler processed it. And the cache for product listings was invalidated. All automatically — the product handler just returned a tuple."

### Action: Create an Order

Go to **Orders** → **Create** → fill in:
- Customer ID: "customer-123"
- Amount: 49.99
- Description: "Keyboard stand purchase"

Submit.

### Observe

Events page shows:
- `OrderCreated` (green badge)

> "Same pattern. The order handler returned `(Result<Order>, OrderCreated?)`, the event was published, and the audit and notification handlers picked it up asynchronously via SQS. The dashboard stats update automatically."

### Action: Switch Back to Dashboard

Click **Dashboard** — show updated totals reflecting the new order and product.

> "The dashboard refreshes automatically when events arrive. The Reports module fetches fresh data from Orders and Products through the mediator — no direct module dependencies."

---

## Part 5: Middleware Pipeline Deep Dive (3 min)

### Show: ObservabilityMiddleware

Open [samples/CleanArchitectureSample/src/Common.Module/Middleware/ObservabilityMiddleware.cs](samples/CleanArchitectureSample/src/Common.Module/Middleware/ObservabilityMiddleware.cs)

### Script

> "Every request flows through a middleware pipeline. This is the Observability middleware — it has three hooks: `Before`, `After`, and `Finally`.
>
> `Before` runs first and returns a `Stopwatch`. That return value is automatically passed as a parameter to `After` and `Finally` — that's state passing across the pipeline, without any manual wiring.
>
> `After` runs on success. `Finally` runs always — like a try/finally block. If the handler took more than 100ms, it logs a warning."

### Show: ValidationMiddleware

Open [samples/CleanArchitectureSample/src/Common.Module/Middleware/ValidationMiddleware.cs](samples/CleanArchitectureSample/src/Common.Module/Middleware/ValidationMiddleware.cs)

> "Validation middleware checks data annotations on your messages — `[Required]`, `[Range]`, `[StringLength]`. If validation fails, it short-circuits: returns `Result.Invalid()` and the handler never executes."

### Show: Message with Validation

Open [samples/CleanArchitectureSample/src/Orders.Module/Messages/OrderMessages.cs](samples/CleanArchitectureSample/src/Orders.Module/Messages/OrderMessages.cs)

```csharp
public record CreateOrder(
    [Required] [StringLength(50, MinimumLength = 3)] string CustomerId,
    [Required] [Range(0.01, 1000000)] decimal Amount,
    [Required] [StringLength(200, MinimumLength = 5)] string Description
)
```

> "Standard .NET validation attributes on a plain record. The middleware picks them up automatically. No FluentValidation dependency, no validators to register."

### Show: Middleware Ordering

> "Middleware is ordered with declarative dependencies — `OrderBefore` and `OrderAfter` — instead of fragile magic numbers. The pipeline ends up being:"

```
RetryMiddleware (wraps everything)
  └─ CachingMiddleware (cache-aside)
       └─ ObservabilityMiddleware (logging + timing)
            └─ ValidationMiddleware (short-circuit on invalid input)
                 └─ Module-scoped middleware
                      └─ Handler
```

---

## Part 6: Custom Attribute-Triggered Middleware (2 min)

### Show: CachedAttribute and CachingMiddleware

Open the `CachedAttribute` definition in Common.Module:

```csharp
[UseMiddleware(typeof(CachingMiddleware))]
public sealed class CachedAttribute : Attribute
{
    public int DurationSeconds { get; set; } = 300;
    public bool SlidingExpiration { get; set; }
}
```

### Script

> "Here's something powerful. `[Cached]` and `[Retry]` aren't built into the framework — they're plain attributes you define yourself. The `[UseMiddleware]` meta-attribute links them to their middleware class. The middleware is marked `ExplicitOnly = true` so it only runs when the attribute is present.
>
> This means you can create your own cross-cutting concerns with the same pattern: define an attribute, point it at your middleware, and decorate any handler method. Zero configuration."

### Show: Caching in Action

Open ProductHandler and find `GetProductCatalog`:

```csharp
[Cached(DurationSeconds = 60)]
public async Task<Result<ProductCatalogSummary>> HandleAsync(GetProductCatalog query, ...)
{
    // Simulates 500ms expensive computation
}
```

> "This query simulates an expensive 500ms computation. With `[Cached(DurationSeconds = 60)]`, the first call takes 500ms, but every subsequent call returns instantly from the hybrid cache — in-memory L1 backed by Redis L2. On a cache miss, it hits L1, then L2, then finally executes the handler."

### Action: Demo Caching (optional live)

Call `GET /api/products/catalog` in the browser's Scalar UI (or watch logs):
- First call: ~500ms (check Aspire traces)
- Second call: ~1ms (served from cache)

---

## Part 7: Authorization (1 min)

### Script

> "Authorization is built in. Each module sets `AuthorizationRequired = true` at the assembly level — every handler requires auth by default. Individual handlers opt out with `[HandlerAllowAnonymous]` for public endpoints like health checks and product listings. Sensitive operations get role-based access with `[HandlerAuthorize(Roles = ["Admin"])]`."

### Show: Contrast Anonymous vs Authorized

```csharp
// Public — no auth required
[HandlerAllowAnonymous]
public async Task<Result<Product>> HandleAsync(GetProduct query, ...)

// Admin + Manager only
[HandlerAuthorize(Roles = ["Admin", "Manager"])]
public async Task<(Result<Product>, ProductCreated?)> HandleAsync(CreateProduct command, ...)

// Admin only
[HandlerAuthorize(Roles = ["Admin"])]
public async Task<(Result, ProductDeleted?)> HandleAsync(DeleteProduct command, ...)
```

> "When unauthorized, the handler isn't invoked — the generated code returns `Result.Unauthorized()` or `Result.Forbidden()` before the pipeline even starts. This is enforced at compile time in the generated interceptors, so there's zero performance overhead."

---

## Part 8: Endpoint Generation (2 min)

### Show: Scalar API Reference

Navigate to the Scalar API docs (find URL from Aspire Dashboard, typically at `/scalar/v1`).

### Script

> "I didn't write a single API route. Every endpoint you see here was generated by the source generator from the handler methods. It infers the HTTP method from the message name: `Get*` → GET, `Create*` → POST, `Update*` → PUT, `Delete*` → DELETE. The route is inferred from the endpoint group and parameter names."

### Show: Generated Route Examples

| Handler Method | Generated Endpoint |
|---|---|
| `HandleAsync(GetOrders)` | `GET /api/orders` |
| `HandleAsync(GetOrder)` | `GET /api/orders/{orderId}` |
| `HandleAsync(CreateOrder)` | `POST /api/orders` |
| `HandleAsync(UpdateOrder)` | `PUT /api/orders/{orderId}` |
| `HandleAsync(DeleteOrder)` | `DELETE /api/orders/{orderId}` |
| `HandleAsync(GetDashboardReport)` | `GET /api/reports` |
| `HandleAsync(SearchCatalog)` | `GET /api/reports/search-catalog` |

> "Result types map to HTTP status codes automatically. `Result.Ok()` → 200, `Result.Created()` → 201, `Result.NotFound()` → 404, `Result.Invalid()` → 422, `Result.Unauthorized()` → 401. No manual `Results.Ok()` or `Results.NotFound()` wrapping."

### Show: Endpoint Group + Filter

```csharp
[HandlerEndpointGroup("Orders", EndpointFilters = [typeof(SetRequestedByFilter)])]
public class OrderHandler(IOrderRepository repository) { ... }
```

> "Endpoint groups control the route prefix and let you attach endpoint filters — these are ASP.NET Core endpoint filters, not mediator middleware. `SetRequestedByFilter` reads the authenticated user from `HttpContext` and populates a `RequestedBy` property on messages that implement `IHasRequestedBy`."

---

## Part 9: Real-Time Streaming (1 min)

### Show: ClientEventStreamHandler

Open [samples/CleanArchitectureSample/src/Api/Handlers/ClientEventStreamHandler.cs](samples/CleanArchitectureSample/src/Api/Handlers/ClientEventStreamHandler.cs)

```csharp
[HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
public async IAsyncEnumerable<ClientEvent> Handle(
    GetEventStream message,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(
        cancellationToken: cancellationToken))
    {
        yield return new ClientEvent(evt.GetType().Name, evt);
    }
}
```

### Script

> "This is the entire streaming handler. It returns `IAsyncEnumerable<ClientEvent>` and the `ServerSentEvents` attribute tells the endpoint generator to use `TypedResults.ServerSentEvents()`. The mediator's `SubscribeAsync` API yields every notification matching `IDispatchToClient` as it's published — from any handler, any module.
>
> The browser connects with `EventSource('/api/events/stream')` and gets a live feed of every domain event. That's what powers the real-time updates on the dashboard, the event log page, and the toast notifications."

### Action: Show Events Page

Switch to `https://localhost:5199/events` — show events streaming in real-time as you perform actions.

---

## Part 10: Distributed Features — The New Stuff (5 min)

### Script: Architecture Overview

> "Now let's talk about the new distributed capabilities. In Aspire, we have 3 API replicas and 3 worker replicas — separate processes. The API replicas serve HTTP traffic and enqueue work. The worker replicas process queues. They share the same codebase but run in different modes.
>
> Two patterns: **Distributed Queues** for work offload, and **Distributed Notifications** for event fan-out."

### Show: Aspire Dashboard

Open the Aspire Dashboard and show:
- 3 `api-0`, `api-1`, `api-2` replicas
- 3 `worker-0`, `worker-1`, `worker-2` replicas
- `localstack` container (SQS + SNS)
- `redis` container

### Show: AppHost/Program.cs

Open [samples/CleanArchitectureSample/src/AppHost/Program.cs](samples/CleanArchitectureSample/src/AppHost/Program.cs)

> "The AppHost defines the topology. Three API replicas with `--mode api`. Three worker replicas with `--mode worker`. LocalStack provides SQS and SNS. Redis stores job state and serves as the L2 cache. All wired through Aspire resource references."

---

### 10a: Distributed Queues — Async Processing

### Show: AuditEventHandler with [Queue]

Open [samples/CleanArchitectureSample/src/Common.Module/Handlers/AuditEventHandler.cs](samples/CleanArchitectureSample/src/Common.Module/Handlers/AuditEventHandler.cs)

```csharp
[Queue]
public class AuditEventHandler(IAuditService auditService, ILogger<AuditEventHandler> logger)
{
    public async Task HandleAsync(OrderCreated evt, CancellationToken cancellationToken)
    {
        await auditService.LogAsync(new AuditEntry("OrderCreated", evt.OrderId, ...));
    }
}
```

### Script

> "Adding `[Queue]` to a handler class is all it takes to make it asynchronous. The handler code itself is identical — no queue-specific logic. When `OrderCreated` is published, the queue middleware serializes the message, enqueues it to SQS, and returns immediately. The worker replicas — running in a different process — pick it up and execute the same handler pipeline with all the middleware.
>
> The handler doesn't know or care whether it's running inline or from a queue. Your business logic stays clean."

### Show: Queue Configuration Options

> "The `[Queue]` attribute has a rich configuration surface:"

```csharp
[Queue(
    Concurrency = 5,          // 5 parallel consumers
    MaxAttempts = 3,           // 1 try + 2 retries
    TimeoutSeconds = 30,       // Visibility timeout
    RetryPolicy = QueueRetryPolicy.Exponential,
    TrackProgress = true       // Enable job state tracking
)]
```

> "'RetryPolicy' supports none, fixed delay, and exponential backoff with jitter to prevent thundering herd. Messages that exceed max attempts are dead-lettered with full context — original headers, failure reason, timestamps."

---

### 10b: Job Progress Tracking

### Show: DemoExportJobHandler

Open [samples/CleanArchitectureSample/src/Common.Module/Handlers/DemoExportJobHandler.cs](samples/CleanArchitectureSample/src/Common.Module/Handlers/DemoExportJobHandler.cs)

```csharp
[Queue(TrackProgress = true, Concurrency = 5, TimeoutSeconds = 10)]
public class DemoExportJobHandler(ILogger<DemoExportJobHandler> logger)
{
    public async Task<Result> HandleAsync(
        DemoExportJob message,
        QueueContext queueContext,
        CancellationToken ct)
    {
        for (int i = 1; i <= message.Steps; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(message.StepDelayMs, ct);

            int percent = (int)((double)i / message.Steps * 100);
            await queueContext.ReportProgressAsync(percent, $"Step {i}/{message.Steps}");
        }

        return Result.Ok();
    }
}
```

### Script

> "With `TrackProgress = true`, the worker tracks the full lifecycle of each job in Redis — queued, processing, progress percentage, completed, failed, or cancelled. `QueueContext` is injected as a handler parameter, giving you `ReportProgressAsync()` for live progress updates.
>
> Let's see it live."

### Action: Open Queue Dashboard

Navigate to `https://localhost:5199/queues`.

> "This is the queue dashboard — entirely built from mediator endpoints in `QueueDashboardHandler`. It shows every registered queue worker, real-time throughput sparklines, and job state."

### Action: Enqueue Demo Jobs

Click **Enqueue 10 Jobs** button.

> "Watch the active jobs section — each job shows a progress bar that updates in real-time as the worker reports progress. Each job has a unique ID, start time, elapsed duration, and the current step message."

**Observe:**
- Jobs appearing in "Active" with progress bars filling
- Progress messages updating: "Step 3/10", "Step 7/10"
- Jobs completing and moving to "Recent" section with green status
- Some jobs failing (simulated transient errors) and being retried
- Rare critical errors going to dead letter

> "The throughput sparklines update live — green for processed, red for failed, orange for dead-lettered. You can see the workers processing across all replicas."

### Action: Cancel a Job

Find an active job and click the **Cancel** button.

> "Cancellation is cooperative. The dashboard requests cancellation through the job state store in Redis. The worker polls for cancellation every 5 seconds — or on every progress report. When detected, it fires the handler's CancellationToken, the handler observes it, and the job is marked as cancelled. No force-kill."

**Observe:** Job status changes to "Cancelled" (yellow/orange badge).

---

### 10c: Distributed Notifications — Event Fan-Out

### Show: Domain Events with IDistributedNotification

Open [samples/CleanArchitectureSample/src/Common.Module/Events/DomainEvents.cs](samples/CleanArchitectureSample/src/Common.Module/Events/DomainEvents.cs)

```csharp
public record OrderCreated(string OrderId, string CustomerId, decimal Amount, DateTime CreatedAt)
    : IDistributedNotification, IDispatchToClient;
```

### Script

> "`IDistributedNotification` is a marker interface. When an event implements it, the `DistributedNotificationWorker` intercepts the local publish and broadcasts it to all replicas via SNS. Every replica gets its own SQS subscription queue — SNS fans out to all of them.
>
> Two layers prevent infinite loops: the HostId header skips self-delivery, and a reference identity set prevents re-broadcasting messages received from the bus.
>
> This is what makes the SSE stream work across replicas. If Replica 1 handles the request and publishes `OrderCreated`, Replicas 2 and 3 also receive it — so every connected browser gets the real-time update regardless of which replica its SSE connection is on."

### Action: Demonstrate Multi-Replica Fan-Out

1. In the Aspire Dashboard, open logs for two different API replicas
2. Create an order in the frontend
3. Show that `OrderCreated` appears in the logs/traces of ALL replicas

> "One replica handled the HTTP request, but all three received the event. That's distributed notifications in action."

---

### 10d: Cache Invalidation Across Replicas

### Show: ProductCacheInvalidationHandler

Open [samples/CleanArchitectureSample/src/Products.Module/Handlers/ProductHandler.cs](samples/CleanArchitectureSample/src/Products.Module/Handlers/ProductHandler.cs) — find the cache invalidation handler (or separate file).

### Script

> "When a product is updated on Replica 1, `ProductUpdated` fans out via SNS to all replicas. Each replica runs `ProductCacheInvalidationHandler`, which explicitly invalidates the affected cache entries. The hybrid cache has an in-memory L1 layer per-replica and a shared Redis L2 layer — the invalidation handler clears both."

> "Without distributed notifications, Replica 2's L1 cache would serve stale data until TTL expiry. With this pattern, cache coherence is immediate."

---

## Part 11: Cross-Module Communication via Mediator (1 min)

### Show: ReportHandler

Open [samples/CleanArchitectureSample/src/Reports.Module/Handlers/ReportHandler.cs](samples/CleanArchitectureSample/src/Reports.Module/Handlers/ReportHandler.cs) — find `GetDashboardReport`:

```csharp
public async Task<Result<DashboardReport>> HandleAsync(GetDashboardReport query, CancellationToken ct)
{
    var ordersResult = await mediator.InvokeAsync(new GetOrders(), ct);
    var productsResult = await mediator.InvokeAsync(new GetProducts(), ct);
    // ... aggregate and return
}
```

### Script

> "The Reports module has no direct dependency on Orders or Products internals. It sends messages through the mediator — the same way an HTTP client would call an API. This is what makes a modular monolith work: the modules are independent, and you could extract any module into a separate service by replacing the mediator call with an HTTP call. The message contracts are the boundary."

---

## Part 12: Aspire Observability (1 min)

### Show: Aspire Dashboard — Traces

Open the Aspire Dashboard traces view. Create an order and find the trace.

### Script

> "Because the mediator integrates with OpenTelemetry, every handler invocation creates a span. Distributed notifications propagate the W3C trace context across replicas, so you can follow an event from the original request through SNS to all replicas in a single distributed trace.
>
> Queue workers also propagate trace context — a job enqueued by an API replica carries its trace ID through SQS to the worker replica. The entire request lifecycle is visible in one trace."

### Show: Distributed Trace

Find a trace that shows:
- HTTP request on API replica
- Handler execution
- Event publish to SNS
- Event received on other replicas
- Queue enqueue + dequeue on worker

---

## Part 13: Recap & Close (1 min)

### Script

> "Let's recap what we've seen:
>
> **Zero boilerplate** — plain handler classes, discovered by naming convention, no interfaces or base classes.
>
> **Near-direct-call performance** — 3.5 nanosecond command dispatch via source generators and interceptors.
>
> **Rich middleware pipeline** — observability, validation, caching, retry — all composable with state passing, short-circuiting, and custom attributes.
>
> **Auto-generated API endpoints** — handlers become minimal API routes with Result-to-HTTP status mapping.
>
> **Cascading events** — tuple returns for decoupled, event-driven architectures.
>
> **Real-time streaming** — `IAsyncEnumerable` handlers for Server-Sent Events.
>
> **Distributed queues** — `[Queue]` for async processing with retry, dead-lettering, progress tracking, and cancellation.
>
> **Distributed notifications** — `IDistributedNotification` for event fan-out across replicas, enabling real-time features and cache coherence.
>
> **Full observability** — OpenTelemetry traces that follow messages across replicas, queues, and pub/sub.
>
> All of this from a library that generates everything at compile time. Your handler code stays simple. The complexity is handled by the source generator.
>
> Check out the docs at foundatio.dev, and the sample app is in the GitHub repo under `samples/CleanArchitectureSample`. Thanks for watching."

---

## Appendix: Backup Demo Scenarios

### If Something Goes Wrong with Aspire

Run the API standalone without distributed infrastructure:

```bash
cd samples/CleanArchitectureSample/src/Api
dotnet run
```

This uses in-memory queues and pub/sub — everything still works, just single-process.

### Payment Retry Demo

> "The payment handler simulates transient failures — 60% of first attempts fail. With `[Retry(MaxAttempts = 5, DelayMs = 100)]`, it automatically retries with exponential backoff until it succeeds."

1. Create an order
2. Process a payment (via API)
3. Watch logs show retry attempts succeeding on attempt 2 or 3

### Validation Short-Circuit Demo

Try creating an order with invalid data:
- Empty customer ID
- Negative amount
- Description too short

Show the 422 response with validation errors — handler never executed.

### Caching Performance Demo

1. Call `GET /api/products/catalog` — note the ~500ms response (Aspire trace)
2. Call again — note the ~1ms response (cache hit)
3. Update a product — cache invalidated
4. Call again — ~500ms (cache miss, recomputed)

### Dead Letter Demo

If a queue job fails with `Result.CriticalError()`, it's immediately dead-lettered. Show the dead-letter count in the queue dashboard.
