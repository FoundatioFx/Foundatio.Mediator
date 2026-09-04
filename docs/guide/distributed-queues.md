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

With no transport registered this uses an in-memory queue, which is right for development and tests. For production add a [transport provider](./distributed-transports) before `AddDistributedQueues()`.

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
2. Sends it to a queue named after the message type (`ProcessOrder`), or `QueueName` if set.
3. Returns `Result.Accepted("Message queued")` immediately.

A worker receives the message and runs the handler through the full middleware pipeline with a fresh DI scope. Authorization runs on the enqueuing node, so the worker skips it.

Queue handlers must return `void`, `Task`, `Result`, or `Result<T>`. Anything else is rejected at enqueue time.

## Queue Configuration

```csharp
[Queue(
    QueueName = "order-processing",    // default: message type name
    Group = "orders",                  // for worker selection, see Scaling Out
    Concurrency = 5,                   // concurrent handlers per worker (default 1)
    PrefetchCount = 10,                // messages per receive (default: Concurrency)
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
| Attempts exceed `MaxAttempts` | Dead-letter |
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

A queue carries one message no matter how many handlers accept it. Publishing `OrderCreated` with two `[Queue]` handlers on it sends one message; the worker dispatches it to both.

Handlers may be declared on an interface or base type. The message header `fm-message-type` names the concrete type, and the worker deserializes to it before dispatching to every handler on the queue whose parameter type accepts it:

```csharp
public interface IOrderEvent { string OrderId { get; } }
public record OrderCreated(string OrderId) : IOrderEvent;
public record OrderShipped(string OrderId) : IOrderEvent;

[Queue(Group = "audit")]
public class OrderAuditHandler
{
    // One queue named "IOrderEvent"; receives OrderCreated and OrderShipped as their concrete types
    public Task HandleAsync(IOrderEvent evt, IAuditLog audit, CancellationToken ct) => audit.WriteAsync(evt, ct);
}
```

Only types a registered handler can accept are ever deserialized; the header cannot make the worker load arbitrary types.

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

With `AutoRenewTimeout` on (the default) the worker renews the visibility timeout at two thirds of `TimeoutSeconds` for as long as the handler runs, and keeps renewing after a failed renewal. A handler may run for hours on a 30-second timeout. `TimeoutSeconds` is capped at 12 hours, the SQS maximum.

## Progress Tracking and Cancellation

`TrackProgress = true` records job state in an `IQueueJobStateStore`: `Queued` at enqueue, `Processing`, `Completed`, `Failed`, or `Cancelled`, with progress, attempt, error message, and a heartbeat on every renewal and progress report. The default store is in-memory; use [Redis](./distributed-transports#redis) for more than one node, or implement the interface over a store you already have.

The job id comes back in the accepted result's `Location`:

```csharp
var result = await mediator.InvokeAsync<Result>(new GenerateReport("monthly"), ct);
var jobId = result.Location!;

var state = await stateStore.GetJobStateAsync(jobId, ct);
Console.WriteLine($"{state.Status} {state.Progress}% {state.ProgressMessage}");

await stateStore.RequestCancellationAsync(jobId, ct);
// The worker observes the request on the next progress report or its cancellation poll (every 5 s)
// and cancels the handler's CancellationToken. A cancelled job completes the message; it is not retried.
```

Attach tenant, user, or any other context to jobs so a store can index them:

```csharp
.AddDistributedQueues(o => o.JobMetadataProvider = message => message is ITenantMessage t
    ? new Dictionary<string, string> { ["tenant"] = t.TenantId, ["user"] = t.RequestedBy }
    : null);
```

The dictionary is stored as `QueueJobState.Metadata`.

## Single Flight with [QueueLock]

Because delivery is at least once, work that must never run twice concurrently gets a distributed lock:

```csharp
public record GenerateBankFile(string Bank) : IHaveLockKey
{
    public string LockKey => $"bank-file:{Bank}";
}

[Queue(Group = "exports")]
[QueueLock(LifetimeSeconds = 120)]
public class GenerateBankFileHandler
{
    public Task<Result> HandleAsync(GenerateBankFile cmd, IBankFiles files, CancellationToken ct) => files.GenerateAsync(cmd.Bank, ct);
}
```

The lock key is `Key` on the attribute, then the message's `IHaveLockKey.LockKey`, then queue name plus message id. The lock is renewed while the handler runs and released afterwards. When another worker already holds it, the message is **completed without running the handler**: that work is already happening. Set `AcquireTimeoutSeconds` to wait instead of giving up immediately.

Register an `IQueueLockProvider` over your lock service (Redis, a database) as a singleton. The in-memory provider is registered automatically only when the in-memory queue client is in use, because a process-local lock is only safe with a process-local queue.

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
        if (headers.TryGetValue("x-tenant", out var tenant))
            callContext.Set(new TenantContext(tenant));
    }
}

builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddQueueHeaderProvider<TenantHeaderProvider>();
```

Anything placed in the `CallContext` is available as a parameter on the handler and its middleware, exactly like `QueueContext`. Every message also carries `fm-correlation-id` (the current trace id, or a new id) and W3C `traceparent`.

## Middleware Integration

Queued handlers run through the same middleware pipeline as local handlers, on the worker. Middleware can tell where it is:

```csharp
[Middleware]
public class ObservabilityMiddleware
{
    public Stopwatch Before(object message, QueueContext? queue)
    {
        Log.Information("Handling {Type} from {Source}", message.GetType().Name, queue is null ? "local" : queue.QueueName);
        return Stopwatch.StartNew();
    }

    public void After(object message, Stopwatch sw) => Log.Information("Handled in {Elapsed}ms", sw.ElapsedMilliseconds);
}
```

`QueueContext` is `null` for local invocations.

## Options

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues(o =>
    {
        o.Workers = WorkerSelection.Parse(builder.Configuration["Distributed:Workers"]); // which workers run here
        o.ResourcePrefix = "myapp-prod";              // prefixes every queue name
        o.ShutdownTimeout = TimeSpan.FromSeconds(60); // drain window for in-flight handlers on stop
        o.EnqueueReadyTimeout = TimeSpan.FromSeconds(30);
        o.JobStateExpiry = TimeSpan.FromHours(24);
        o.QueueDepthPollInterval = TimeSpan.FromSeconds(30); // queue.depth.* metrics; Zero disables
        o.JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    });
```

If workers are disabled or filtered and no transport is registered, registration fails: messages sent to an in-memory queue with no worker in the same process would be lost. Set `AllowInMemoryWithoutWorkers` in tests that want exactly that.
