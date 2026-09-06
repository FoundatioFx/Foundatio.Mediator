# Modular Monolith Sample

A working modular monolith that shows Foundatio.Mediator in a realistic multi-module application, and proves the distributed-queue scenarios a real monolith needs when it moves its background work onto SQS/SNS: one build that runs as an API node or as any set of workers, tracked long-running jobs, retries and dead letters you can inspect and replay, single-flight locks for money movement, tenant propagation, and per-host visibility across replicas.

Four modules communicate only through the mediator. No module references another's handlers or data layer.

## What This Sample Demonstrates

### Distributed queues and notifications

| Scenario | Where to look |
| -------- | ------------- |
| **Config-only topology** — API node, worker groups, or everything in one process, from one setting | `Api/AppOptions.cs`, `Api/Program.cs` (`WorkerSelection.Parse`), `AppHost/Program.cs` (`SAMPLE_TOPOLOGY`) |
| **Tracked long-running job** — progress, heartbeat, cancel, host that ran it | `DemoExportJobHandler`, Redis job state via `UseRedisJobState()`, Queues page → Jobs tab |
| **Interface-typed handler** — one queue for every `IOrderEvent` | `OrderAuditHandler.HandleAsync(IOrderEvent)`, `order-events` queue |
| **Shared queue, several handlers, one message** | `OrderConfirmationHandler` + `OrderFulfillmentHandler` on `QueueName = "order-created"` |
| **Distributed notifications with an explicit rule set** | `Program.cs`: `IncludeNotificationsFromAssemblyOf<IOrderEvent>()`, `Exclude<ProductStockChanged>()`; product events keep `IDistributedNotification` |
| **`[QueueLock]` single-flight** | `GenerateBankFileHandler`, `Api/Infrastructure/RedisQueueLockProvider.cs`, "Bank file" control |
| **Retry schedule, dead letters, replay, purge** | `FlakyWebhookHandler` (`MaxAttempts = 3, RetryDelays = "1s,3s"`), Queues page → Dead letters tab |
| **Header provider + job metadata** — tenant follows the message | `Api/Infrastructure/TenantHeaderProvider.cs`, `TenantContext`, header tenant selector |
| **Observability and scale-out** | `ServiceDefaults` registers `DistributedMetrics.MeterName`; `DemoJobCompleted(JobId, QueueName, HostId, Tenant)` feeds "Completions by host" |
| **Queue administration through the mediator** | `QueueDashboardHandler` delegates to `GetQueueOverview`, `ListDeadLetters`, `ReplayDeadLetters`, `PurgeDeadLetters`, `CancelQueueJob` |

### Core mediator features

| Feature | Where to look |
| ------- | ------------- |
| **Cascading events** | `OrderHandler` returns `(Result<Order>, OrderCreated?)` tuples |
| **Cross-module queries** | `ReportHandler` fetches Orders and Products via `mediator.InvokeAsync()` |
| **Middleware pipeline** | `ObservabilityMiddleware` (Before/After/Finally with state), `ValidationMiddleware` (short-circuit) |
| **Attribute-triggered middleware** | `[Cached]` and `[Retry]` are plain attributes linked to middleware via `[UseMiddleware]` |
| **Caching with cross-node invalidation** | `[Cached(DurationSeconds = 30)]` on product queries; `ProductCacheInvalidationHandler` runs on every node |
| **Retry middleware and named policies** | `[Retry(MaxAttempts = 5)]` on `PaymentHandler`, `[Retry(PolicyName = "aggressive")]` on `UpdateOrder` |
| **Authorization** | `[HandlerAuthorize(Roles = ["Admin"])]`, `[HandlerAllowAnonymous]`, global `AuthorizationRequired = true` |
| **Message validation** | `[Required]`, `[Range]`, `[StringLength]` on records, enforced by `ValidationMiddleware` |
| **Endpoint generation, groups, filters** | `MapMediatorEndpoints()`; `[HandlerEndpointGroup("Orders", EndpointFilters = [typeof(RequestDurationFilter)])]` |
| **Message enrichment** | `SetRequestedByMiddleware` stamps the caller onto immutable records via `HandlerResult.ContinueWith` |
| **Streaming SSE endpoint** | `Api/Handlers/EventHandler.cs` turns `IDispatchToClient` notifications into `GET /api/events` |
| **Result pattern** | `Result.NotFound()`, `Result.Invalid()`, `Result.Error()` — no exceptions for business outcomes |

## Project Structure

```text
src/
├── Common.Module/                         # Cross-cutting: events, middleware, queue handlers, shared services
│   ├── Events/DomainEvents.cs             # IOrderEvent + order events, product events, worker events
│   ├── TenantContext.cs                   # Tenant/user a piece of work runs for; restored on workers
│   ├── HostInfo.cs                        # "worker-exports:12345" style host id for events and progress
│   ├── Handlers/
│   │   ├── DemoExportJobHandler.cs        # [Queue(Group="exports", TrackProgress=true, Concurrency=2)]
│   │   ├── ImportProductCatalogHandler.cs # [Queue(Group="imports", TrackProgress=true)]
│   │   ├── FlakyWebhookHandler.cs         # [Queue(Group="events", MaxAttempts=3, RetryDelays="1s,3s")]
│   │   ├── GenerateBankFileHandler.cs     # [Queue(Group="exports")] [QueueLock]
│   │   ├── OrderAuditHandler.cs           # [Queue(QueueName="order-events")] HandleAsync(IOrderEvent)
│   │   ├── OrderConfirmationHandler.cs    # [Queue(QueueName="order-created")] — shared queue, handler 1
│   │   ├── AuditEventHandler.cs           # [Queue(Group="events")] — product events, one queue per type
│   │   ├── NotificationEventHandler.cs    # [Queue(Group="events")] — stock alerts, order updates
│   │   └── QueueDashboardHandler.cs       # /api/queues: delegates to the library's administration messages
│   ├── Messages/JobMessages.cs            # DemoExportJob, ImportProductCatalog, DeliverWebhook, GenerateBankFile
│   └── Middleware/                        # Observability, Validation, Caching, Retry, SetRequestedBy
│
├── Orders.Module/
│   └── Handlers/
│       ├── OrderHandler.cs                # CRUD with cascading events, auth, retry
│       ├── OrderFulfillmentHandler.cs     # [Queue(QueueName="order-created")] — shared queue, handler 2; publishes OrderShipped
│       └── PaymentHandler.cs              # Transient failures for the retry demo
│
├── Products.Module/                       # Product catalog; Redis-backed repository; cache invalidation handler
├── Reports.Module/                        # Cross-module aggregation via mediator queries only
│
├── Api/                                   # Composition root; the one deployable
│   ├── Program.cs                         # AddMediator().AddDistributedQueues().AddDistributedNotifications().UseAws().UseRedisJobState()
│   ├── AppOptions.cs                      # --mode api|worker|both, --workers <selection>
│   ├── Infrastructure/
│   │   ├── RedisQueueLockProvider.cs      # IQueueLockProvider: SET NX PX, compare-and-delete, compare-and-PEXPIRE
│   │   └── TenantHeaderProvider.cs        # IQueueHeaderProvider + JobMetadataProvider source
│   └── Handlers/EventHandler.cs           # SSE stream of IDispatchToClient notifications
│
├── AppHost/                               # Aspire: LocalStack (SQS/SNS), Redis, and either topology
├── ServiceDefaults/                       # OpenTelemetry (incl. the Foundatio.Mediator.Distributed meter), health checks
└── Web/                                   # SvelteKit SPA: Queues page, Live Events, orders, products, reports
```

## Running the Sample

Prerequisites: .NET 10 SDK, Node.js 20+, Docker.

```bash
cd samples/CleanArchitectureSample/src/AppHost
dotnet run                          # split topology (default)
dotnet run --launch-profile single  # everything in one process
```

The frontend is at `https://localhost:5199`; every other URL is assigned by Aspire and shown in its dashboard. Demo users: `admin`/`admin` (Admin), `user`/`user` (User). Reads on the Queues page are anonymous; enqueueing, cancelling, replaying, and purging need the Admin role.

### Two topologies, one project

`AppHost/Program.cs` reads `SAMPLE_TOPOLOGY`:

| `SAMPLE_TOPOLOGY` | Resources | Args |
| ----------------- | --------- | ---- |
| `split` (default) | `api` ×2 | `--mode api` (Workers = none) |
| | `worker-exports` ×2 | `--mode worker --workers exports` |
| | `worker-imports` | `--mode worker --workers imports` |
| | `worker-events` | `--mode worker --workers events` |
| `single` | `api` | none (API + every worker) |

Every resource runs the same `Api` project. `AppOptions` turns `--mode`/`--workers` into one string, and `Program.cs` hands it to the library:

```csharp
.AddDistributedQueues(opts =>
    opts.Workers = WorkerSelection.Parse(options.Workers ?? builder.Configuration["Distributed:Workers"]))
```

`WorkerSelection` accepts `all`, `none`, or a comma-separated list of group or queue names with `!` for exclusions (`exports,imports`, `!events`). Names match `[Queue(Group = ...)]` or the queue name. Moving a group out of process is therefore only configuration: an API node sets `Distributed__Workers=none` (or `--mode api`), and the process that should run the group sets `Distributed__Workers=exports`. Nothing in the modules changes, and the API node still enqueues to every queue because the topology is registered on every node. An API node's row on the Queues page shows no "worker here" badge; a worker node's row does.

Running without a real transport is refused on purpose: with `Workers` other than `all` and no `IQueueClient` registered, `AddDistributedQueues` throws, because messages enqueued to an in-memory queue with no worker in the process would be lost.

## Distributed Queue Walkthrough

Open the **Queues** page (sign in as admin) with **Live Events** in a second tab. Each control below maps to one scenario.

### 1. Tracked long-running job: progress, heartbeat, cancel

```csharp
[Queue(Group = "exports", TrackProgress = true, TimeoutSeconds = 60, Concurrency = 2)]
public class DemoExportJobHandler(HostInfo host, ILogger<DemoExportJobHandler> logger)
{
    public async Task<Result> HandleAsync(DemoExportJob message, QueueContext queueContext, TenantContext tenant, IMediator mediator, CancellationToken ct)
    {
        for (int i = 1; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(jitter, ct);
            await queueContext.ReportProgressAsync(percent, $"Step {i} of {steps} on {host.HostId}", ct);
        }

        await mediator.PublishAsync(new DemoJobCompleted(queueContext.JobId!, queueContext.QueueName, host.HostId, tenant.TenantId), ct);
        return Result.Ok();
    }
}
```

- `TrackProgress = true` gives every message a job id and a `QueueJobState` in the store — Redis here via `UseRedisJobState()`, so any node can read it.
- `QueueContext.ReportProgressAsync(percent, message)` writes progress, heartbeats the message's visibility timeout, and throws `OperationCanceledException` if cancellation was requested from the dashboard. The worker also auto-renews the timeout at two thirds of `TimeoutSeconds` while the handler runs, so a 60-second timeout is not a 60-second limit.
- Invoking a `[Queue]` handler enqueues instead of running it. The result confirms acceptance. `QueueDashboardHandler` uses `mediator.EnqueueAsync` and reads the job ID from the typed receipt. The handler’s own generated endpoint answers `202 Accepted`; an application status URL is separate from the job identifier.
- Returning `Result.Error(...)` (or throwing) abandons the message for retry; `Result.CriticalError(...)`, `Result.Invalid(...)`, and other non-transient statuses dead-letter it at once. The export simulates both at a low rate.
- **UI:** "Tracked export jobs → Enqueue 1". The Jobs tab shows the progress bar, the step message with the host that is running it, `tenant=`/`user=` metadata chips, and a Cancel button. Cancellation is cooperative: the worker notices on its next progress report or poll (5 s) and marks the job Cancelled without retrying it.

### 2. Interface-typed handler: one queue for every order event

```csharp
public interface IOrderEvent : IDispatchToClient { string OrderId { get; } }
public record OrderCreated(...) : IOrderEvent;
public record OrderShipped(string OrderId, string Carrier, string TrackingNumber, DateTime ShippedAt) : IOrderEvent;

[Queue(QueueName = "order-events", Group = "events")]
public class OrderAuditHandler(IAuditService auditService, HostInfo host, ...)
{
    public async Task HandleAsync(IOrderEvent evt, TenantContext tenant, CancellationToken ct) { ... }
}
```

Publishing any `IOrderEvent` enqueues one message to `order-events`, tagged with the concrete type. The worker deserializes it as that type (the library resolves the name against the declared interface, so no type registration is needed) and dispatches to every handler whose parameter type is assignable. **UI:** create an order; the Live Events page shows `OrderCreated` and, a couple of seconds later, `OrderShipped` (see the next scenario) — both audited by the same handler. The `order-events` row shows one handler and two message types passing through it.

### 3. Shared queue: one message, several handlers

```csharp
// Common.Module
[Queue(QueueName = "order-created", Group = "events")]
public class OrderConfirmationHandler(...) { public async Task HandleAsync(OrderCreated evt, TenantContext tenant, CancellationToken ct) { ... } }

// Orders.Module
[Queue(QueueName = "order-created", Group = "events")]
public class OrderFulfillmentHandler(IOrderRepository repository, ...) { public async Task HandleAsync(OrderCreated evt, IMediator mediator, TenantContext tenant, CancellationToken ct) { ... } }
```

Two handlers in two modules declare the same `QueueName` with identical settings (the library validates that at startup and refuses mismatches). When `OrderCreated` is published, the registry selects the first ordered matching enqueue pipeline, so the queue receives **one** message; the worker then runs both handlers from it. The `order-created` row on the Queues page lists both handlers. Fulfillment publishes `OrderShipped` from the worker, which takes the same path as any API-side publish: enqueued for `OrderAuditHandler`, and distributed to the API nodes' event feeds.

The default queue name is the message type name, so `AuditEventHandler.HandleAsync(ProductCreated)` and `NotificationEventHandler.HandleAsync(ProductCreated)` already share a `ProductCreated` queue the same way; the explicit name just makes it visible.

### 4. Distributed notifications: keep chatty events off the bus

```csharp
.AddDistributedNotifications(notifications => notifications
    .IncludeNotificationsFromAssemblyOf<IOrderEvent>()   // every domain event in Common.Module
    .Exclude<ProductStockChanged>())                      // exclusions always win
```

Three ways to select what crosses SNS, all used here:

- **Marker interface.** Product events implement `IDistributedNotification`, because `ProductCacheInvalidationHandler` must run on every node to clear each node's L1 cache.
- **Rule.** Order and worker events are plain `INotification`s included by rule. `IncludeNotificationsFromAssemblyOf<T>()` includes every concrete notification in an assembly; `Include<T>()` and `IncludeAssignableTo<T>()` are the narrower alternatives.
- **Exclusion.** `ProductStockChanged` is published on every stock change alongside `ProductUpdated`, and only queued handlers consume it. Those are enqueued by the publishing node, so sending the event to every other node would be pure noise. `Exclude<T>()` beats every include rule.

A notification that arrives from the bus is re-published locally, but its `[Queue]` handlers are **not** enqueued again — the originating node already did that. The event feed on every API replica shows the same events, whichever replica handled the request.

A receiving node deserializes a bus message either because the type was registered at startup or because its own rules would distribute that type, so a handler declared on an interface such as `IOrderEvent` receives concrete events published by other nodes even when no handler names them.

### 5. `[QueueLock]`: single-flight for money movement

```csharp
public record GenerateBankFile(string Bank, string BatchId) : IHaveLockKey
{
    public string GetLockKey() => $"bank-file:{Bank}";
}

[Queue(Group = "exports", Concurrency = 2, TimeoutSeconds = 60)]
[QueueLock]
public class GenerateBankFileHandler(...) { ... }
```

`QueueLockMiddleware` acquires the lock before the handler runs, renews it while the handler runs, and releases it afterwards. When the lock is held by another delivery, the worker keeps its queue lease and retries acquisition with jitter. Both distinct messages eventually execute, sharing no deduplication shortcut. `AcquireTimeoutSeconds` controls each acquisition wait. The library only ships a process-local provider (auto-registered for in-memory queues); a real transport needs a shared one. `Api/Infrastructure/RedisQueueLockProvider.cs` is 60 lines: `SET NX PX` to acquire, a Lua compare-and-delete to release, a Lua compare-and-`PEXPIRE` to renew, registered as a singleton.

**UI:** "Bank file → Enqueue 2" puts two `GenerateBankFile` messages for the same bank on the queue together. Exactly one `BankFileGenerated` appears on Live Events, the queue's processed count goes up by two, and the worker log has `Lock 'bank-file:first-national' is held by another worker; completing message ... without running`.

### 6. Retry schedule, dead letters, replay, purge

```csharp
[Queue(Group = "events", MaxAttempts = 3, RetryDelays = "1s,3s")]
public class FlakyWebhookHandler(...)
{
    public async Task<Result> HandleAsync(DeliverWebhook message, QueueContext queueContext, IMediator mediator, CancellationToken ct)
    {
        if (!Uri.TryCreate(message.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Result.Invalid($"'{message.Url}' is not an absolute http(s) URL");   // dead-lettered at once

        if (queueContext.DequeueCount <= message.FailTimes)
            return Result.Error($"{uri.Host} returned 503 on attempt {queueContext.DequeueCount}");   // retried

        await mediator.PublishAsync(new WebhookDelivered(message.Url, queueContext.DequeueCount, host.HostId), ct);
        return Result.Ok();
    }
}
```

`RetryDelays` selects the schedule policy: attempt 1 fails → wait 1 s → attempt 2 fails → wait 3 s → attempt 3 fails → the next receive exceeds `MaxAttempts` and the message moves to `DeliverWebhook-dead-letter` with the reason, attempt count, original queue, correlation id (the enqueuing request's trace id), and job id as headers. `QueueContext.DequeueCount` is the attempt number; `MaxAttempts` is also on the context.

Which results retry: `Result.Error`, `Result.Unavailable`, and `Result.RateLimited` (and thrown exceptions) abandon with backoff. Everything else that is not success — `Invalid`, `CriticalError`, `NotFound`, ... — is a content error and dead-letters immediately.

**UI:** "Flaky webhook" with `FailTimes = 5` dead-letters in about seven seconds; `FailTimes = 1` recovers on the second attempt and `WebhookDelivered` shows `attempts: 2`; `not-a-url` dead-letters at once with reason `Invalid: ...`. The red dead-letter count opens the **Dead letters** tab: message type, reason, attempts, when, correlation id, body preview, **Replay** per message, **Replay all**, and **Purge**. Replay sends the body back to the original queue from attempt 1 — a message that fails for the same reason lands in the dead-letter queue again, which is the point: replay is for failures whose cause has been fixed. Messages that dead-letter during a replay run are left for the next decision rather than looped.

The dashboard does none of this itself. The library ships mediator messages with no HTTP surface — `GetQueueOverview`, `GetQueueDetail`, `ListQueueJobs`, `GetQueueJob`, `CancelQueueJob`, `ListDeadLetters`, `ReplayDeadLetters`, `PurgeDeadLetters` — and `QueueDashboardHandler` exposes them under `/api/queues` with the host's authorization:

```csharp
[HandlerAuthorize(Roles = ["Admin"])]
[HandlerEndpoint(HandlerMethod.Post, "dead-letters/replay")]
public async Task<Result<DeadLetterReplayResult>> HandleAsync(ReplayQueueDeadLetters command, IMediator mediator, CancellationToken ct)
    => await mediator.InvokeAsync<Result<DeadLetterReplayResult>>(new ReplayDeadLetters(command.QueueName, command.Max, command.MessageId), ct);
```

| Endpoint | Delegates to | Auth |
| -------- | ------------ | ---- |
| `GET /api/queues/queues`, `GET /api/queues/queue?queueName=` | `GetQueueOverview`, `GetQueueDetail` | anonymous |
| `GET /api/queues/job-dashboard?queueName=`, `GET /api/queues/queue-job/{jobId}` | job state store, `GetQueueJob` | anonymous |
| `GET /api/queues/dead-letters?queueName=` | `ListDeadLetters` | anonymous |
| `GET /api/queues/host` | which process answered, and its `Workers` | anonymous |
| `POST /api/queues/job/{jobId}/cancel-job` | `CancelQueueJob` | Admin |
| `POST /api/queues/dead-letters/replay`, `POST /api/queues/dead-letters/purge` | `ReplayDeadLetters`, `PurgeDeadLetters` | Admin |
| `POST /api/queues/enqueue/{exports,imports,flaky-webhook,bank-files}` | invokes the `[Queue]` handler, returns queue name and job ids | Admin |

### 7. Header provider and job metadata: the tenant follows the message

```csharp
public sealed class TenantHeaderProvider(IHttpContextAccessor httpContextAccessor) : IQueueHeaderProvider
{
    public void Enrich(object message, IDictionary<string, string> headers)
    {
        var tenant = Resolve(httpContextAccessor.HttpContext);   // X-Tenant header, or the ambient TenantContext on a worker
        headers["app-tenant"] = tenant.TenantId;
        headers["app-user"] = tenant.User;
    }

    public void Restore(IReadOnlyDictionary<string, string> headers, CallContext callContext)
    {
        var tenant = new TenantContext(headers["app-tenant"], headers["app-user"]);
        callContext.Set(tenant);          // handlers take it as a TenantContext parameter
        TenantContext.Current = tenant;   // follow-on messages the handler sends inherit it
    }
}
```

Registered with `.AddQueueHeaderProvider<TenantHeaderProvider>()`. Every provider's `Enrich` runs on enqueue and `Restore` runs on the worker before the handler. Handlers declare a `TenantContext tenant` parameter: on the worker it comes from the `CallContext`; inline, DI resolves it from the current request (`Program.cs` registers a scoped factory). When a worker-side handler publishes or enqueues — `OrderFulfillmentHandler` publishing `OrderShipped` — the ambient `TenantContext.Current` is what `Enrich` picks up, so the tenant survives a chain of queues.

`DistributedQueueOptions.JobMetadataProvider` records the same two values on every tracked job at enqueue time; the Jobs tab shows them as `tenant=` and `user=` chips. **UI:** change the tenant selector in the header (sent as `X-Tenant`), enqueue an export, and read the worker log line `Starting export job ... for globex/admin on worker-exports:...`.

### 8. Observability and scale-out

`ServiceDefaults` adds `.AddMeter(DistributedMetrics.MeterName)`, so the Aspire dashboard's Metrics view shows `queue.messages.enqueued`, `queue.messages.processed`, `queue.messages.failed`, `queue.messages.dead_lettered`, `queue.messages.in_flight`, `queue.handler.duration`, the sampled `queue.depth.*` gauges, and `notifications.published`/`received`, tagged by queue, message type, and group. Enqueue and process spans are linked (not parented), so an hour-long job does not stretch the request's trace.

**UI:** "Tracked export jobs → Enqueue 20". With two `worker-exports` replicas at concurrency 2, four jobs run at a time. Each completion publishes `DemoJobCompleted(JobId, QueueName, HostId, Tenant)`; the "Completions by host" panel tallies them per `worker-exports:<pid>`, the Live Events page shows the host badge on every event, and the header of the Queues page names the API replica that answered each poll.

## Mediator Feature Walkthrough

### Cascading events

When a handler returns a tuple, the extra values are published as events. The publishing module has no knowledge of who reacts:

```csharp
public async Task<(Result<Order>, OrderCreated?)> HandleAsync(CreateOrder command, ...)
{
    var order = new Order(...);
    await repository.AddAsync(order, cancellationToken);
    return (order, new OrderCreated(order.Id, command.CustomerId, command.Amount, DateTime.UtcNow));
}
```

`UpdateProduct` returns two optional events; `ProductStockChanged` is `null`, and therefore not published, unless stock changed. Queue handlers must return `void`, `Task`, `Result`, or `Result<T>` — a queued handler that needs to publish does so through `IMediator`, as the worker-side handlers here do.

### Cross-module queries

`ReportHandler` aggregates Orders and Products without touching their repositories:

```csharp
var ordersResult = await mediator.InvokeAsync(new GetOrders(), ct);
var productsResult = await mediator.InvokeAsync(new GetProducts(), ct);
```

### Middleware pipeline

`ObservabilityMiddleware` passes the `Stopwatch` returned from `Before` into `After` and `Finally`, and takes an optional `QueueContext` parameter to log whether a message came from HTTP, the bus, or a queue. `ValidationMiddleware` short-circuits with `Result.Invalid(...)` before the handler runs. Ordering uses `OrderBefore`/`OrderAfter` instead of numbers:

```text
RetryMiddleware (Execute, Order=0)
  └─ CachingMiddleware (Execute, Order=100)
       └─ ObservabilityMiddleware (Before/After/Finally)
            └─ ValidationMiddleware (Before)
                 └─ OrdersModuleMiddleware / ProductsModuleMiddleware (module-scoped)
                      └─ Handler
```

On a worker the same pipeline runs: `QueueMiddleware` (Order −100) sees the `QueueContext` and lets the message through, `QueueLockMiddleware` (Order −90) takes the lock, then the rest.

### Caching

`[Cached]` opts a handler into `CachingMiddleware`, a `HybridCache` (L1 memory + L2 Redis) cache-aside. The key is the message type plus its JSON, so identical queries share an entry on every node. Invalidation is explicit and cross-node: `ProductCacheInvalidationHandler` handles the distributed product events on every replica and calls `CachingMiddleware.InvalidateAsync(new GetProducts())` and friends.

### Retry

`PaymentHandler` fails about 60% of first attempts; `[Retry(MaxAttempts = 5, DelayMs = 100)]` retries in-process with exponential backoff and jitter. `[Retry(PolicyName = "aggressive")]` looks a policy up from `IResiliencePolicyProvider`. This is in-process retry of a synchronous call; the queue's retry (scenario 6) is the durable, cross-process kind.

### Authorization

Every module sets `AuthorizationRequired = true`. Handlers opt out with `[HandlerAllowAnonymous]` or narrow with `[HandlerAuthorize(Roles = [...])]`; both work at class and method level, and the generated endpoints get matching `RequireAuthorization`/`AllowAnonymous` calls. Authorization runs on the enqueuing node; the worker runs handlers with authorization skipped, because the caller was already checked.

### Endpoint generation

`MapMediatorEndpoints()` generates minimal API routes from handlers: verb inferred from the message name (`Get*` → GET, `Create*` → POST, ...), route from the group and the message's `*Id` properties, and `Result` statuses mapped to HTTP. `[HandlerEndpointGroup]` sets prefix, tags, and endpoint filters; `[HandlerEndpoint(HandlerMethod.Post, "dead-letters/replay")]` pins a route; `[HandlerEndpoint(Exclude = true)]` keeps a handler off HTTP, which the internal queue handlers use. `RequestDurationFilter` adds `X-Request-Duration-Ms`.

### Custom attribute-triggered middleware

`[Cached]` and `[Retry]` are plain attributes with `[UseMiddleware(typeof(...))]`; the middleware is `ExplicitOnly = true`, so it runs only where the attribute appears. `[Queue]` and `[QueueLock]` from the library follow the same pattern.

### Streaming SSE endpoint

```csharp
public class EventHandler(IMediator mediator)
{
    [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
    public async IAsyncEnumerable<ClientEvent> Handle(GetEventStream message, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(cancellationToken))
            yield return new ClientEvent(evt.GetType().Name, evt);
    }
}
```

`mediator.SubscribeAsync<IDispatchToClient>()` yields every notification implementing the marker, including ones re-published from the bus. The browser connects with `new EventSource('/api/events')`.

### Result pattern

Handlers return `Result`/`Result<T>`; `Result.NotFound()`, `Result.Invalid()`, `Result.Error()` map to 404/422/500 on HTTP and to dead-letter-or-retry on a queue.

## Module Dependencies

```text
Api (composition root)
  ├── Common.Module
  ├── Orders.Module
  ├── Products.Module
  └── Reports.Module

Reports.Module
  ├── Common.Module
  ├── Orders.Module   (message types only)
  └── Products.Module (message types only)

Orders.Module / Products.Module
  └── Common.Module   (events, middleware, TenantContext, HostInfo)

Common.Module (no module dependencies)
```

See `DEMO.md` for a walkthrough script that covers the scenarios in order.
