# Modular Monolith Sample

A working modular monolith that showcases Foundatio.Mediator's features in a realistic multi-module application. Four independent modules communicate exclusively through the mediator — no module directly references another's handlers or data layer.

## What This Sample Demonstrates

| Mediator Feature | Where to Look |
| ---------------- | ------------- |
| **Cascading events** | `OrderHandler` returns `(Result<Order>, OrderCreated?)` tuples |
| **Cross-module event handlers** | `AuditEventHandler` and `NotificationEventHandler` consume events from all modules |
| **Cross-module queries** | `ReportHandler` fetches data from Orders and Products via `mediator.InvokeAsync()` |
| **Middleware pipeline** | `ObservabilityMiddleware` (Before/After/Finally with state), `ValidationMiddleware` (short-circuit) |
| **Custom attribute-triggered middleware** | `[Cached]` and `[Retry]` are plain attributes linked to middleware via `[UseMiddleware]` |
| **Caching middleware** | `[Cached(DurationSeconds = 30)]` on product queries, manual `CachingMiddleware.Invalidate()` on writes |
| **Retry middleware** | `[Retry(MaxAttempts = 5)]` on `PaymentHandler` with exponential backoff + jitter |
| **Named retry policies** | `[Retry(PolicyName = "aggressive")]` on `UpdateOrder` |
| **Authorization** | `[HandlerAuthorize(Roles = ["Admin"])]`, `[HandlerAllowAnonymous]`, global `AuthorizationRequired = true` |
| **Message validation** | `[Required]`, `[Range]`, `[StringLength]` on message records, enforced by `ValidationMiddleware` |
| **Endpoint generation** | `MapMediatorEndpoints()` auto-generates minimal API routes from handlers |
| **Endpoint groups & filters** | `[HandlerEndpointGroup("Orders", EndpointFilters = [typeof(SetRequestedByFilter)])]` |
| **Middleware ordering** | `OrderBefore`/`OrderAfter` declarative dependencies between middleware |
| **Module-scoped middleware** | `OrdersModuleMiddleware`, `ProductsModuleMiddleware` run only for their module's messages |
| **Multiple cascading events** | `UpdateProduct` returns `(Result<Product>, ProductUpdated?, ProductStockChanged?)` |
| **Result pattern** | `Result.NotFound()`, `Result.Invalid()`, `Result.Error()` — no exceptions for business logic |
| **Streaming SSE endpoint** | `ClientEventStreamHandler` turns `IDispatchToClient` events into a real-time SSE stream |
| **Assembly configuration** | `[assembly: MediatorConfiguration(AuthorizationRequired = true, ...)]` per module |
| **Distributed notifications** | Domain events implement `IDistributedNotification` — fan out across all replicas via SNS+SQS |
| **Async queue handlers** | `[Queue]` on `AuditEventHandler` / `NotificationEventHandler` — processed via SQS |
| **Job progress tracking** | `DemoExportJobHandler` with `TrackProgress = true`, progress reporting via `QueueContext` |
| **Queue dashboard** | `QueueDashboardHandler` — built-in endpoints for listing queues, job state, counters, and cancellation |
| **Queue dashboard UI** | SvelteKit page with real-time throughput sparklines, job progress bars, and cancellation |
| **Aspire orchestration** | `AppHost` runs 3 API replicas + LocalStack (SQS/SNS) + Redis for local development |

## Project Structure

```text
src/
├── Common.Module/               # Cross-cutting middleware, events, shared services
│   ├── Events/
│   │   └── DomainEvents.cs      # OrderCreated, ProductUpdated, etc. (IDistributedNotification)
│   ├── Handlers/
│   │   ├── AuditEventHandler.cs        # [Queue] — async audit logging via SQS
│   │   ├── NotificationEventHandler.cs # [Queue] — async notification delivery via SQS
│   │   ├── DemoExportJobHandler.cs     # [Queue(TrackProgress=true)] — long-running job with progress
│   │   ├── QueueDashboardHandler.cs    # Queue monitoring endpoints (list, stats, cancel)
│   │   └── HealthHandler.cs            # [HandlerAllowAnonymous] health check
│   ├── Middleware/
│   │   ├── ObservabilityMiddleware.cs  # Before/After/Finally with Stopwatch state
│   │   ├── ValidationMiddleware.cs     # Short-circuits on invalid messages
│   │   ├── CachingMiddleware.cs        # Execute middleware with cache-aside pattern
│   │   └── RetryMiddleware.cs          # Execute middleware with backoff policies
│   └── ServiceConfiguration.cs
│
├── Orders.Module/               # Order processing bounded context
│   ├── Handlers/
│   │   ├── OrderHandler.cs      # CRUD with cascading events, auth, retry
│   │   └── PaymentHandler.cs    # Simulates transient failures for retry demo
│   ├── Messages/
│   │   └── OrderMessages.cs     # Commands/queries with validation attributes
│   ├── Middleware/
│   │   └── OrdersModuleMiddleware.cs   # Module-scoped middleware
│   └── ServiceConfiguration.cs
│
├── Products.Module/             # Product catalog bounded context
│   ├── Handlers/
│   │   └── ProductHandler.cs    # CRUD with caching, cache invalidation, multi-event tuples
│   ├── Messages/
│   │   └── ProductMessages.cs
│   ├── Middleware/
│   │   └── ProductsModuleMiddleware.cs
│   └── ServiceConfiguration.cs
│
├── Reports.Module/              # Cross-module aggregation (no data layer)
│   ├── Handlers/
│   │   └── ReportHandler.cs     # Queries other modules via mediator only
│   ├── Messages/
│   │   └── ReportMessages.cs
│   └── ServiceConfiguration.cs
│
├── Api/                         # ASP.NET Core composition root
│   ├── Program.cs               # AddMediator(), SQS/SNS, Aspire, MapMediatorEndpoints()
│   └── Handlers/
│       └── ClientEventStreamHandler.cs  # Streaming SSE endpoint for real-time events
│
├── AppHost/                     # Aspire orchestrator (3 API replicas + LocalStack + Redis)
│   └── Program.cs
│
├── ServiceDefaults/             # Aspire service defaults (OpenTelemetry, health checks)
│   └── Extensions.cs
│
└── Web/                         # SvelteKit SPA frontend
```

## Feature Walkthrough

### 1. Cascading Events

When a handler returns a tuple, extra values are automatically published as events. The publishing module has no knowledge of which handlers will react:

```csharp
// OrderHandler.cs — returns result + event
public async Task<(Result<Order>, OrderCreated?)> HandleAsync(CreateOrder command, ...)
{
    var order = new Order(...);
    await repository.AddAsync(order, cancellationToken);

    // OrderCreated is published automatically after this handler completes
    return (order, new OrderCreated(order.Id, command.CustomerId, command.Amount, DateTime.UtcNow));
}
```

Multiple handlers in Common.Module react without the Orders module knowing they exist:

```csharp
// AuditEventHandler.cs — consumes OrderCreated
public async Task HandleAsync(OrderCreated evt, IAuditService auditService, CancellationToken ct)
{
    await auditService.LogAsync(new AuditEntry("OrderCreated", evt.OrderId, ...));
}

// NotificationEventHandler.cs — also consumes OrderCreated
public async Task HandleAsync(OrderCreated evt, INotificationService notificationService, CancellationToken ct)
{
    await notificationService.SendAsync(new Notification($"New order {evt.OrderId}", ...));
}
```

`UpdateProduct` shows returning multiple events conditionally — `ProductStockChanged` is only published when stock actually changes:

```csharp
public async Task<(Result<Product>, ProductUpdated?, ProductStockChanged?)> HandleAsync(UpdateProduct command, ...)
{
    // ...update logic...
    var stockEvent = stockChanged
        ? new ProductStockChanged(command.ProductId, oldQuantity, newQuantity, DateTime.UtcNow)
        : null;  // null events are not published

    return (updatedProduct, updatedEvent, stockEvent);
}
```

### 2. Cross-Module Communication

`ReportHandler` aggregates data from Orders and Products without ever touching their repositories — all communication goes through the mediator:

```csharp
public class ReportHandler(IMediator mediator, ILogger<ReportHandler> logger)
{
    public async Task<Result<DashboardReport>> HandleAsync(GetDashboardReport query, CancellationToken ct)
    {
        // Query other modules via mediator — no direct dependencies on their internals
        var ordersResult = await mediator.InvokeAsync(new GetOrders(), ct);
        var productsResult = await mediator.InvokeAsync(new GetProducts(), ct);

        if (!ordersResult.IsSuccess)
            return Result.Error($"Failed to fetch orders: {ordersResult.Message}");

        var orders = ordersResult.Value ?? [];
        var products = productsResult.Value ?? [];

        return new DashboardReport(
            TotalOrders: orders.Count,
            TotalRevenue: orders.Sum(o => o.Amount),
            // ... aggregate data from both modules
        );
    }
}
```

### 3. Middleware Pipeline

#### Observability (Before/After/Finally with State Passing)

The return value from `Before` is passed as a parameter to `After` and `Finally`:

```csharp
[Middleware(OrderBefore = [typeof(ValidationMiddleware)])]
public class ObservabilityMiddleware
{
    public Stopwatch Before(object message, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        logger.LogInformation("Handling {MessageType} in {HandlerType}", ...);
        return Stopwatch.StartNew();  // This Stopwatch is passed to After and Finally
    }

    public void After(object message, Stopwatch stopwatch, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        stopwatch.Stop();
        if (stopwatch.ElapsedMilliseconds > 100)
            logger.LogWarning("Slow handler: {HandlerType} took {ElapsedMs}ms", ...);
    }

    public void Finally(object message, Stopwatch? stopwatch, Exception? exception, ILogger<IMediator> logger)
    {
        stopwatch?.Stop();
        if (exception != null)
            logger.LogError(exception, "Error handling {MessageType} after {ElapsedMs}ms", ...);
    }
}
```

#### Validation (Short-Circuiting)

Messages decorated with `[Required]`, `[Range]`, etc. are validated before reaching the handler:

```csharp
[Middleware(OrderAfter = [typeof(ObservabilityMiddleware)])]
public static class ValidationMiddleware
{
    public static HandlerResult Before(object message)
    {
        if (MiniValidator.TryValidate(message, out var errors))
            return HandlerResult.Continue();

        // Short-circuit: handler never executes, pipeline returns Result.Invalid(...)
        return HandlerResult.ShortCircuit(Result.Invalid(validationErrors));
    }
}
```

Used with validated messages:

```csharp
public record CreateOrder(
    [Required] [StringLength(50, MinimumLength = 3)] string CustomerId,
    [Required] [Range(0.01, 1000000)] decimal Amount,
    [Required] [StringLength(200, MinimumLength = 5)] string Description
) : ICommand<Result<Order>>, IHasRequestedBy;
```

#### Middleware Ordering

Middleware dependencies are declared with `OrderBefore`/`OrderAfter` instead of fragile numeric values:

```text
RetryMiddleware (Execute, Order=0)        — outermost, wraps everything
  └─ CachingMiddleware (Execute, Order=100) — wraps pipeline, cache-aside
       └─ ObservabilityMiddleware (Before/After/Finally, OrderBefore=[ValidationMiddleware])
            └─ ValidationMiddleware (Before, OrderAfter=[ObservabilityMiddleware])
                 └─ OrdersModuleMiddleware / ProductsModuleMiddleware (module-scoped)
                      └─ Handler
```

### 4. Caching

The `[Cached]` attribute opts specific handlers into the caching middleware. Cache invalidation is manual and explicit:

```csharp
// Read: cached for 30 seconds
[Cached(DurationSeconds = 30)]
public async Task<Result<Product>> HandleAsync(GetProduct query, ...) { ... }

// Write: explicitly invalidate related cached queries
public async Task<(Result<Product>, ProductCreated?)> HandleAsync(CreateProduct command, ...)
{
    await repository.AddAsync(product, cancellationToken);
    CachingMiddleware.Invalidate(new GetProducts());  // Clear the list cache
    return (product, new ProductCreated(...));
}
```

### 5. Retry with Policies

Handlers opt into retry via `[Retry]`. The `PaymentHandler` simulates transient failures to demonstrate this:

```csharp
// Inline configuration
[Retry(MaxAttempts = 5, DelayMs = 100)]
public Task<Result<string>> HandleAsync(ProcessPayment command, ...)
{
    // ~60% of first attempts fail with a transient error
    if (Random.Shared.NextDouble() < 0.6)
        throw new InvalidOperationException("Transient payment gateway error");

    return Task.FromResult<Result<string>>($"PAY-{Guid.NewGuid():N}"[..16]);
}

// Named policy (configured in DI)
[Retry(PolicyName = "aggressive")]
public async Task<(Result<Order>, OrderUpdated?)> HandleAsync(UpdateOrder command, ...) { ... }
```

Named policies are registered in `ServiceConfiguration`:

```csharp
services.AddSingleton<IResiliencePolicyProvider>(
    new ResiliencePolicyProviderBuilder()
        .WithPolicy("aggressive", p => p.WithMaxAttempts(10).WithExponentialDelay(TimeSpan.FromMilliseconds(50)).WithJitter())
        .Build());
```

### 6. Authorization

Global auth is enabled via assembly configuration. Individual handlers opt out with `[HandlerAllowAnonymous]`:

```csharp
// Assembly-level: all handlers require auth by default
[assembly: MediatorConfiguration(AuthorizationRequired = true)]

// Handler-level: role-based access
[HandlerAuthorize(Roles = ["User", "Admin"])]
public async Task<(Result<Order>, OrderCreated?)> HandleAsync(CreateOrder command, ...) { ... }

// Opt out: public endpoints
[HandlerAllowAnonymous]
public async Task<Result<Product>> HandleAsync(GetProduct query, ...) { ... }
```

### 7. Endpoint Generation

`MapMediatorEndpoints()` generates minimal API endpoints from all discovered handlers. Endpoint groups and filters control routing:

```csharp
// Handlers grouped and filtered at the class level
[HandlerEndpointGroup("Orders", EndpointFilters = [typeof(SetRequestedByFilter)])]
public class OrderHandler(IOrderRepository repository)
{
    // Generated as: POST /api/orders, GET /api/orders/{orderId}, etc.
}
```

The `SetRequestedByFilter` enriches messages from the HTTP context before the handler runs — an endpoint filter, not mediator middleware.

### 8. Custom Attribute-Triggered Middleware

Both `[Cached]` and `[Retry]` are plain C# attributes that you define yourself — they aren't baked into the framework. The pattern has two parts:

**1. Define the attribute** with `[UseMiddleware]` pointing to the middleware it activates:

```csharp
[UseMiddleware(typeof(CachingMiddleware))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class CachedAttribute : Attribute
{
    public int DurationSeconds { get; set; } = 300;
    public bool SlidingExpiration { get; set; }
}
```

**2. Mark the middleware as `ExplicitOnly`** so it only runs when the attribute is present:

```csharp
[Middleware(Order = 100, ExplicitOnly = true)]
public class CachingMiddleware
{
    public async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo)
    {
        // Read settings from the attribute on the handler method
        var attr = handlerInfo.HandlerMethod.GetCustomAttribute<CachedAttribute>();
        var duration = TimeSpan.FromSeconds(attr?.DurationSeconds ?? 300);

        var cacheKey = GetCacheKey(message);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var result = await next();
        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions().SetAbsoluteExpiration(duration));
        return result;
    }
}
```

This is what makes the handler code so clean — decorating a method opts it into the middleware without any other configuration:

```csharp
[Cached(DurationSeconds = 30)]                         // activates CachingMiddleware
public async Task<Result<Product>> HandleAsync(GetProduct query, ...) { ... }

[Retry(MaxAttempts = 5, DelayMs = 100)]                 // activates RetryMiddleware
public Task<Result<string>> HandleAsync(ProcessPayment command, ...) { ... }

[Retry(PolicyName = "aggressive")]                      // same middleware, named policy
public async Task<(Result<Order>, OrderUpdated?)> HandleAsync(UpdateOrder command, ...) { ... }
```

You can create your own attributes following the same pattern — define an attribute with `[UseMiddleware(typeof(YourMiddleware))]`, mark the middleware `ExplicitOnly = true`, and any handler method decorated with your attribute will have that middleware applied.

### 9. Streaming Handler (Real-Time SSE)

A streaming handler uses `IAsyncEnumerable<T>` to push domain events to clients in real time via Server-Sent Events. The mediator's built-in `SubscribeAsync<T>` yields events as they're published anywhere in the system:

```csharp
public class ClientEventStreamHandler(IMediator mediator)
{
    [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
    public async IAsyncEnumerable<ClientEvent> Handle(
        GetEventStream message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(cancellationToken: cancellationToken))
        {
            yield return new ClientEvent(evt.GetType().Name, evt);
        }
    }
}
```

The pieces that make this work:

- **`IDispatchToClient`** — a marker interface on events that should reach the browser (`OrderCreated`, `ProductUpdated`, etc.)
- **`mediator.SubscribeAsync<IDispatchToClient>()`** — the mediator's subscription API yields every notification matching the type as it's published
- **`EndpointStreaming.ServerSentEvents`** — tells the endpoint generator to wrap the `IAsyncEnumerable` with `TypedResults.ServerSentEvents()`, setting the `text/event-stream` content type
- **`IAsyncEnumerable<ClientEvent>`** — ASP.NET Core streams each yielded item to the client as an SSE message

When any handler in any module publishes a cascading event marked with `IDispatchToClient`, it automatically appears in the SSE stream — no additional wiring needed. The frontend connects using the browser's `EventSource` API:

```javascript
const source = new EventSource('/events/stream');
source.onmessage = (e) => {
    const event = JSON.parse(e.data);
    console.log(event.eventType, event.data);
};
```

### 10. Distributed Notifications (SNS+SQS Fan-Out)

Domain events implement `IDistributedNotification` so they automatically fan out to all replicas in a scale-out cluster. When one replica handles a request and publishes `OrderCreated`, every other replica also receives it — enabling SSE streams, cache invalidation, and other real-time features to work across all instances:

```csharp
// DomainEvents.cs — IDistributedNotification extends INotification
public record OrderCreated(string OrderId, string CustomerId, decimal Amount, DateTime CreatedAt)
    : IDistributedNotification, IDispatchToClient;
```

The `DistributedNotificationWorker` background service bridges local mediator pub/sub with the remote SNS+SQS bus:

- **Outbound**: subscribes to local `IDistributedNotification` events → serializes → publishes to SNS topic
- **Inbound**: subscribes to per-node SQS queue (fed by SNS) → deserializes → publishes locally via `mediator.PublishAsync()`
- **Loop prevention**: two layers prevent infinite re-broadcast — HostId header (skip self-delivery) + reference identity set (skip re-broadcast of bus-received messages)

### 11. Async Queue Handlers (SQS)

Event handlers decorated with `[Queue]` offload processing to an SQS queue, keeping the request path fast. The audit and notification handlers both use this pattern:

```csharp
[Queue]
public class AuditEventHandler(IAuditService auditService, ILogger<AuditEventHandler> logger)
{
    // This runs asynchronously via SQS — the handler that published OrderCreated
    // returns immediately without waiting for audit logging to complete
    public async Task HandleAsync(OrderCreated evt, CancellationToken cancellationToken) { ... }
}
```

The distributed infrastructure is wired up in `Program.cs`:

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddDistributedNotifications()
    .UseAws(aws => aws.ServiceUrl = builder.Configuration["AWS:ServiceURL"]!)
    .UseRedisJobState();
```

### 12. Result Pattern

All handlers return `Result<T>` for business logic outcomes instead of throwing exceptions:

```csharp
public async Task<Result<Order>> HandleAsync(GetOrder query, CancellationToken ct)
{
    var order = await repository.GetByIdAsync(query.OrderId, ct);

    if (order is null)
        return Result.NotFound($"Order {query.OrderId} not found");

    return order;  // Implicit conversion to Result<Order>
}
```

### 13. Job Progress Tracking

The `DemoExportJobHandler` shows a long-running queue job with progress reporting. The `[Queue(TrackProgress = true)]` attribute enables the job state store (Redis in this sample) to track status, progress percentage, and support cancellation:

```csharp
[Queue(TrackProgress = true, Concurrency = 5, TimeoutSeconds = 10)]
public class DemoExportJobHandler(ILogger<DemoExportJobHandler> logger)
{
    public async Task<Result> HandleAsync(DemoExportJob message, QueueContext queueContext, CancellationToken ct)
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

Key points:

- **`QueueContext`** is injected as a handler parameter — provides `ReportProgressAsync()`, `AcknowledgeAsync()`, `RejectAsync()`, `DeferAsync()`
- **`Result.Error()`** tells the worker to abandon and retry; **`Result.CriticalError()`** dead-letters immediately
- **Cancellation** is checked via `CancellationToken` — the queue dashboard can request cancellation through the job state store

### 14. Queue Dashboard

The `QueueDashboardHandler` exposes queue monitoring as mediator endpoints under `/api/queues`. The SvelteKit frontend provides a real-time dashboard with throughput sparklines, job progress bars, and cancellation:

```csharp
[HandlerEndpointGroup("Queues")]
[HandlerAllowAnonymous]
public class QueueDashboardHandler
{
    // GET /api/queues/queues — list all registered queue workers with stats
    [Cached(DurationSeconds = 2)]
    public async Task<Result<List<QueueSummary>>> HandleAsync(GetQueues query, ...) { ... }

    // GET /api/queues/job-dashboard — job state with active/recent jobs
    [Cached(DurationSeconds = 2)]
    public async Task<Result<JobDashboardView>> HandleAsync(GetJobDashboard query, ...) { ... }

    // POST /api/queues/cancel-job — request job cancellation
    public async Task<Result> HandleAsync(CancelJob command, ...) { ... }
}
```

The frontend (`/queues` route) polls these endpoints and renders:

- **Per-queue throughput** — processed/failed/dead-lettered sparkline charts
- **Active jobs** — progress bars with percentage and status message
- **Recent jobs** — completed/failed/cancelled with duration

## Module Dependencies

Modules reference other modules only for message/DTO types — never for handlers, repositories, or services:

```text
Api (composition root)
  ├── Common.Module
  ├── Orders.Module
  ├── Products.Module
  └── Reports.Module

Reports.Module
  ├── Common.Module
  ├── Orders.Module (message types only — GetOrders, Order)
  └── Products.Module (message types only — GetProducts, Product)

Orders.Module / Products.Module
  └── Common.Module (events, middleware, shared interfaces)

Common.Module (no module dependencies)
```

## Running the Sample

### Prerequisites

- .NET 10 SDK
- Node.js 20+ (for the frontend)
- Docker (for Aspire, LocalStack, and Redis)

### Quick Start

The sample runs via Aspire, which orchestrates separate API and worker processes with LocalStack (SQS/SNS) and Redis:

```bash
cd samples/CleanArchitectureSample/src/AppHost
dotnet run
```

This starts:

- **3 API replicas** — serve HTTP endpoints and the SPA frontend (no queue workers)
- **3 Worker replicas** — process all queues (no API endpoints)
- **LocalStack** — provides SQS (async queue processing) and SNS (pub/sub notifications)
- **Redis** — shared persistence, distributed caching, and job state tracking
- **Vite frontend** — SvelteKit SPA at `https://localhost:5199`
- **Aspire Dashboard** — traces, logs, and metrics at the URL shown in terminal output

The API and worker processes share the same `Api` project — the `--mode api`/`--mode worker` argument controls which features are active.

### URLs

All URLs are assigned dynamically by Aspire — check the Aspire Dashboard for the actual ports. The frontend is the exception:

| URL | Description |
| --- | ----------- |
| `https://localhost:5199` | SvelteKit frontend (fixed port) |
| Aspire Dashboard | API endpoints, traces, logs, metrics |

### Try the API

Use the frontend at `https://localhost:5199` or call the API directly (find the API URL from the Aspire Dashboard):

```bash
# Create a product (requires Admin login)
curl -X POST https://localhost:{port}/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Widget","description":"A great widget","price":29.99,"stockQuantity":50}'

# Create an order
curl -X POST https://localhost:{port}/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"customer-123","amount":29.99,"description":"Widget purchase"}'

# Dashboard report (aggregates from both modules)
curl https://localhost:{port}/api/reports

# Search across modules
curl "https://localhost:{port}/api/reports/search-catalog?searchTerm=widget"
```

Demo users: `admin`/`admin` (Admin role), `user`/`user` (User role).

### Watch the Middleware Pipeline

Console output shows the full middleware flow:

```text

info: Handling CreateOrder in OrderHandler
info: Completed CreateOrder in OrderHandler (5ms)
dbug: Auditing OrderCreated event for order abc123
dbug: Sending order confirmation notification for order abc123
```

### Frontend

The SvelteKit frontend (Svelte 5, Tailwind CSS, TypeScript) provides a dashboard, CRUD pages for Orders and Products, a queue dashboard with live job progress, and a live events page. Aspire runs the Vite dev server and proxies `/api/*` requests to the API replicas.
