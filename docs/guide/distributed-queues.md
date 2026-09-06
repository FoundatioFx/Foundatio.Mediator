---
title: "Distributed Queues"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 20
---

# Distributed Queues

Distributed queues offload handler execution to background workers, across processes, containers, or machines. Messages are serialized, sent to a queue, and processed with retries, dead-lettering, visibility timeouts, and optional progress tracking. Your handler code barely changes.

## Installation

```bash
dotnet add package Foundatio.Mediator.Distributed
```

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues();
```

With no transport registered this uses an in-memory queue, which is right for development and tests. For production add a [transport provider](./distributed-transports) before or after `AddDistributedQueues()`.

## Making a Handler Queue-Based

```csharp
[Queue]
public class OrderProcessingHandler
{
    public async Task<Result> HandleAsync(ProcessOrder cmd, IOrderService orders, CancellationToken ct)
    {
        await orders.ProcessAsync(cmd, ct);
        return Result.Ok();
    }
}
```

`mediator.InvokeAsync(new ProcessOrder(...))` now:

1. Serializes the message to JSON.
2. Sends it to an independent subscription queue (`OrderProcessing-ProcessOrder` here), or an explicit `QueueName`.
3. Returns `Result.Accepted("Message queued")` immediately.

A worker receives the message and runs the handler through the full middleware pipeline with a fresh DI scope. Authorization runs on the enqueuing node, so the worker skips it.

Queue handlers support `void`, `Task`, `ValueTask`, `Result`, `Result<T>`, and cascading tuples with a Result first (including awaited forms). Unsupported return types fail during registration. `InvokeAsync<Result<T>>` returns acceptance without the eventual `T`; processing results are not returned to the caller. Cascading events are published only after processing.

## Queue Configuration

```csharp
[Queue(
    QueueName = "order-processing",    // explicit subscription identity
    Group = "orders",                  // for worker selection, see Scaling Out
    Concurrency = 5,                   // concurrent handlers per worker (default 1)
    PrefetchCount = 5,                 // per-receive cap, also bounded by available Concurrency
    MaxAttempts = 3,                   // 1 attempt + 2 retries (default 3); negative = unlimited
    TimeoutSeconds = 30,               // visibility timeout (default 30; max 43200)
    RetryPolicy = QueueRetryPolicy.Exponential,
    RetryDelaySeconds = 5,             // base delay for Fixed and Exponential
    RetryDelays = "5s,1m,15m,30m",     // explicit schedule; sets RetryPolicy = Schedule
    AutoComplete = true,               // complete/abandon from the handler result
    AutoRenewTimeout = true,           // renew the visibility timeout while the handler runs
    TrackProgress = false,             // job state and progress reporting
    Description = "Fulfils paid orders")]
public class OrderProcessingHandler { ... }
```

Handlers that share a `QueueName` share one queue and one worker, and must declare identical settings. Mismatches fail at startup naming both handlers.

## Retries

The worker decides what happens to a message from the handler's result, or its exception:

| Outcome | Action |
| --- | --- |
| Success statuses, or a `void`/`Task` handler that returns | Complete |
| `Error`, `Unavailable`, `RateLimited`, or an unhandled exception | Abandon; redelivered after the retry delay |
| `Invalid`, `BadRequest`, `NotFound`, `Unauthorized`, `Forbidden`, `Conflict`, `CriticalError` | Dead-letter immediately |
| Final allowed attempt fails | Dead-letter immediately, preserving the actual error and attempt count |
| Receive count exceeds `MaxAttempts` after a crash/redelivery | Dead-letter as a safety net |
| Body cannot be deserialized, or names an unknown type | Dead-letter immediately, on the first attempt |

Retry delay policies:

| Policy | Delay before attempt _n_+1 |
| --- | --- |
| `None` | Immediate |
| `Fixed` | `RetryDelaySeconds`, with ±10% jitter |
| `Exponential` | `RetryDelaySeconds × 2^(n-1)`, with ±10% jitter, capped at 15 minutes |
| `Schedule` | The _n_-th entry of `RetryDelays`; the last entry repeats. No jitter |

```csharp
// Reproduce an existing retry policy exactly: 5 s, then 1 m, then 15 m, then 30 m for every further attempt
[Queue(MaxAttempts = 5, RetryDelays = "5s,1m,15m,30m")]
public class ImportBankFileHandler { ... }
```

Use `Result.Conflict(...)` for "somebody else already did this": it dead-letters at once with your message as the reason instead of retrying.

## Dead-Letter Queues

Messages that exhaust their attempts or fail with a non-retryable result move to `{queue}-dead-letter` with the original body and headers plus:

| Header | Meaning |
| --- | --- |
| `fm-dead-letter-reason` | Why it was dead-lettered |
| `fm-dead-lettered-at` | When (ISO 8601) |
| `fm-original-queue-name` | Where it came from |
| `fm-dead-letter-dequeue-count` | How many attempts were made |

Dead letters can be inspected, replayed to their original queue, or purged through the [administration handlers](./distributed-operations). Replayed messages carry `fm-replayed-at`.

## Several Handlers, Interfaces, and Base Types

Each `[Queue]` handler/message pair has its own durable subscription by default. Publishing `OrderCreated` to an audit handler and a webhook handler sends one message to each subscription. A webhook retry does not rerun successful audit work.

Names strip the `Handler`/`Consumer` suffix: `OrderHandler` handling `Order` uses `Order`; `AuditHandler` handling `Order` uses `Audit-Order`. Set `QueueName` to keep an identity stable when renaming source types. Accidental name collisions fail registration. Explicitly naming the same queue on every member opts into a shared execution and retry budget.

Handlers may be declared on an interface or base type. The message header `fm-message-type` names the concrete type, and the worker deserializes to it before dispatching to every handler on the queue whose parameter type accepts it:

```csharp
public interface IOrderEvent { string OrderId { get; } }
public record OrderCreated(string OrderId) : IOrderEvent;
public record OrderShipped(string OrderId) : IOrderEvent;

[Queue(Group = "audit")]
public class OrderAuditHandler
{
    // One subscription named "OrderAudit-IOrderEvent"; receives OrderCreated and OrderShipped as their concrete types
    public Task HandleAsync(IOrderEvent evt, IAuditLog audit, CancellationToken ct) => audit.WriteAsync(evt, ct);
}
```

Only types a registered handler can accept are ever deserialized; the header cannot make the worker load arbitrary types.

For an explicitly shared queue, Publish selects the first ordered matching handler as the enqueue pipeline and sends once. Only that member’s enqueue validation/enrichment runs. The worker runs all matching processing pipelines in order, including `OrderBefore`/`OrderAfter` declarations. Use independent subscriptions when members need different enqueue rules.

Handlers on a shared queue run in sequence. If one fails with a retryable result or an exception, the message is abandoned and every handler runs again on the next attempt, so handlers that share a queue must be idempotent, like any queued handler.

## QueueContext

When the handler runs inside a worker, a `QueueContext` parameter is available:

```csharp
[Queue(TimeoutSeconds = 300, TrackProgress = true)]
public class DataImportHandler
{
    public async Task<Result> HandleAsync(ImportData cmd, QueueContext ctx, IImportService imports, CancellationToken ct)
    {
        var batches = await imports.GetBatchesAsync(cmd.FileId, ct);
        for (int i = 0; i < batches.Count; i++)
        {
            await imports.ProcessBatchAsync(batches[i], ct);
            await ctx.ReportProgressAsync((i + 1) * 100 / batches.Count, $"Batch {i + 1}/{batches.Count}", ct);
        }

        return Result.Ok();
    }
}
```

```csharp
ctx.QueueName          // queue the message came from
ctx.MessageId          // transport message id
ctx.MessageType        // concrete message type
ctx.DequeueCount       // 1 on the first attempt
ctx.MaxAttempts
ctx.VisibilityTimeout
ctx.EnqueuedAt
ctx.JobId              // when TrackProgress is on

await ctx.ReportProgressAsync(ct);                       // heartbeat: renews the visibility timeout and the job's heartbeat
await ctx.ReportProgressAsync(75, "Rendering", ct);      // also records progress; throws OperationCanceledException if cancellation was requested
await ctx.RenewTimeoutAsync(TimeSpan.FromMinutes(5), ct);
await ctx.CompleteAsync(ct);                             // manual lifecycle when AutoComplete = false
await ctx.AbandonAsync(TimeSpan.FromSeconds(30), ct);
```

With `AutoRenewTimeout` on (the default) the worker renews the visibility timeout halfway through the lease, retrying transient failures within the remaining lease window. Known lease or lock loss cancels cooperative handler work. State-store heartbeats cannot block transport renewal. A handler may run for hours on a 30-second timeout. `TimeoutSeconds` is capped at 12 hours, the SQS maximum.

## Progress Tracking and Cancellation

`TrackProgress = true` records job state in an `IQueueJobStateStore`: `Queued` at enqueue, `Processing`, `RetryPending`, `Completed`, `Failed`, `Cancelled`, or `EnqueueUnknown`, with progress, attempt, error message, and a heartbeat on every renewal and progress report. The default store is in-memory. A distributed transport with tracked work requires a shared store and fails startup without one. Use [Redis](./distributed-transports#redis), implement a shared store, or explicitly set `AllowProcessLocalJobStateForDevelopment` for development/tests. Transport and state-store decorators must forward `IsDistributed` and `IsShared`.

Use the typed receipt when the caller needs tracking. Its `JobId` is null for untracked work; `QueueName` is the physical destination. An HTTP status URL is a separate application decision:

```csharp
var result = await mediator.EnqueueAsync(new GenerateReport("monthly"), ct);
if (!result.IsSuccess)
    return; // enqueue validation rejected the message
var jobId = result.Value.JobId!;

var state = await stateStore.GetJobStateAsync(jobId, ct);
Console.WriteLine($"{state.Status} {state.Progress}% {state.ProgressMessage}");

await stateStore.RequestCancellationAsync(jobId, ct);
// The worker checks before execution, then on progress or its cancellation poll (every 5 s)
// and cancels the handler's CancellationToken. A cancelled job completes the message; it is not retried.
```

Attach tenant, user, or any other context to jobs so a store can index them:

```csharp
.AddDistributedQueues(o => o.JobMetadataProvider = message => message is ITenantMessage t
    ? new Dictionary<string, string> { ["tenant"] = t.TenantId, ["user"] = t.RequestedBy }
    : null);
```

The dictionary is stored as `QueueJobState.Metadata`. Retry-pending jobs remain cancellable. Explicit completion or abandonment is authoritative even if later handler code throws. An unconfirmed acknowledgment never reports Completed.

If a send fails after state creation, `QueueEnqueueException.Receipt` identifies the attempted job and its state becomes `EnqueueUnknown` when the store is reachable. The transport may have accepted it; reconcile before blindly retrying. Tracked dead-letter replay through administration creates a new job ID, keeping the original failure record.

## Single Flight with [QueueLock]

Because delivery is at least once, work that must never run twice concurrently gets a distributed lock:

```csharp
public record GenerateBankFile(string Bank) : IHaveLockKey
{
    public string GetLockKey() => $"bank-file:{Bank}";
}

[Queue(Group = "exports")]
[QueueLock(LifetimeSeconds = 120)]
public class GenerateBankFileHandler
{
    public Task<Result> HandleAsync(GenerateBankFile cmd, IBankFiles files, CancellationToken ct) => files.GenerateAsync(cmd.Bank, ct);
}
```

The lock key is `Key` on the attribute, then `IHaveLockKey.GetLockKey()`, then queue name plus message id. The method is not serialized. Contention waits with bounded jitter while retaining the queue lease and retry budget, so distinct messages both eventually run. `AcquireTimeoutSeconds` controls each acquisition wait. The lock is renewed while processing and released afterwards.

Locks coordinate concurrent owners; they are not persistent duplicate detection. Lease-loss cancellation cannot undo side effects already performed. Handlers still need idempotency for at-least-once delivery.

Register an `IQueueLockProvider` over your lock service (Redis, a database) as a singleton. The in-memory provider is used automatically only when the in-memory queue client is in use, because a process-local lock is only safe with a process-local queue.

## Carrying Context in Headers

An `IQueueHeaderProvider` adds headers on enqueue and restores context on the worker, for example a tenant scope or a request id:

```csharp
public sealed class TenantHeaderProvider(IHttpContextAccessor http) : IQueueHeaderProvider
{
    public void Enrich(object message, IDictionary<string, string> headers)
    {
        if (http.HttpContext?.Request.Headers["X-Tenant"] is { Count: > 0 } tenant)
            headers["x-tenant"] = tenant!;
    }

    public void Restore(IReadOnlyDictionary<string, string> headers, CallContext callContext)
    {
        if (!headers.TryGetValue("x-tenant", out var tenant))
            throw new InvalidOperationException("Required tenant header is missing.");
        callContext.Set(new TenantContext(tenant));
    }
}

builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddQueueHeaderProvider<TenantHeaderProvider>();
```

Providers are scoped by default, resolved from the caller’s mediator scope and from a fresh worker scope before handler construction. A restoration exception fails the message. Pass `ServiceLifetime.Singleton` to `AddQueueHeaderProvider` for a stateless singleton. In console hosts using request-scoped providers, register the mediator with `.SetMediatorLifetime(ServiceLifetime.Scoped)` and resolve it inside the caller scope.

Anything placed in the `CallContext` is available as a parameter on the handler and its middleware, exactly like `QueueContext`. Every message also carries `fm-correlation-id` (the current trace id, or a new id) and W3C `traceparent`.

## Middleware Integration

General middleware defaults to `MiddlewareStage.Processing` on queued handlers. Validation or request enrichment before sending opts into `Enqueue`; use `Both` only when repeating the behavior is safe. On ordinary nonqueued handlers, the existing middleware behavior is unchanged.

```csharp
[Middleware(Stage = MiddlewareStage.Enqueue)]
public class OrderValidationMiddleware
{
    public HandlerResult Before(ProcessOrder message)
        => string.IsNullOrWhiteSpace(message.OrderId)
            ? HandlerResult.ShortCircuit(Result.Invalid("OrderId is required"))
            : HandlerResult.Continue();
}
```

The stage applies to the entire lifecycle: construction, Execute, Before, After, Finally, and Before state. Short circuits run applicable cleanup. `HandlerResult.ContinueWith(replacement)` from enqueue middleware changes the serialized message. Processing retry/cache middleware does not wrap enqueueing. Use Result-returning handlers when enqueue validation must return a structured rejection.


## Options

```csharp
builder.Services.AddMediator()
    .ConfigureDistributed(o =>
    {
        o.ResourcePrefix = "myapp-prod"; // queues, topics, subscription infrastructure, Redis state
        o.JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    })
    .AddDistributedQueues(o =>
    {
        o.Workers = WorkerSelection.Parse(builder.Configuration["Distributed:Workers"]); // which workers run here
        o.ShutdownTimeout = TimeSpan.FromSeconds(60); // drain window for in-flight handlers on stop
        o.EnqueueReadyTimeout = TimeSpan.FromSeconds(30);
        o.JobStateExpiry = TimeSpan.FromHours(24);
        o.QueueDepthPollInterval = TimeSpan.FromSeconds(30); // queue.depth.* metrics; Zero disables
        o.QueueOverrides["order-processing"] = queue => queue.Concurrency = 8;
    });
```

If workers are disabled or filtered and no transport is registered, startup fails: messages sent to an in-memory queue with no worker in the same process would be lost. Set `AllowInMemoryWithoutWorkers` in tests that want exactly that.

Shared defaults are configured before queue/notification registration; explicit feature/provider overrides win. Worker selectors use logical subscription or group names without prefixes. Supported text is `all`, `none`, or comma-separated names with `!name` exclusions; malformed expressions and unmatched names fail registration with available-name diagnostics.

## Database and enqueue consistency

A database commit followed by an enqueue is two operations. A process crash or ambiguous send failure can leave one committed without the other. Applications needing atomic business changes plus eventual delivery should write an outbox record in the database transaction and relay it with a stable operation ID. Make consumers idempotent against that ID. This library provides at-least-once transport processing, not a generic transactional outbox or exactly-once side effects.

Start with the executable [DistributedConsoleSample](https://github.com/FoundatioFx/Foundatio.Mediator/tree/main/samples/DistributedConsoleSample), which starts a real host and deterministically waits for completion without Docker.
