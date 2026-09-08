---
title: "Distributed Queues"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 20
---

# Distributed Queues

Configure a native Foundatio message bus, then call `AddMediator().AddDistributedQueues()`. See [transport setup](./distributed-transports).

## Write an ordinary handler

```csharp
public record ExportReport(string ReportId);

[Queue(Group = "exports", TrackProgress = true,
    Concurrency = 4, MaxAttempts = 3, RetryDelays = "5s,30s")]
public class ExportReportHandler
{
    public async Task<Result> HandleAsync(
        ExportReport message, MessageProcessingContext context,
        IReportService reports, CancellationToken ct)
    {
        await context.ReportProgressAsync(10, "Preparing report", ct);
        await reports.ExportAsync(message.ReportId, ct);
        await context.ReportProgressAsync(100, "Report ready", ct);
        return Result.Ok();
    }
}
```

`MessageProcessingContext` comes directly from `Foundatio.Messaging`. Add it only when a handler needs progress, attempt information, headers, or explicit settlement. Ordinary dependencies still come from one fresh DI scope per delivery.

```csharp
var accepted = await mediator.EnqueueAsync(new ExportReport("monthly"), ct);
if (accepted.IsSuccess)
{
    QueueReceipt receipt = accepted.Value;
    // Return HTTP 202 with receipt.QueueName and receipt.JobId.
}
```

`EnqueueAsync` validates the enqueue middleware pipeline and returns `Result<QueueReceipt>`. It does not wait for the worker. A tracked receipt can be queried through the native `IMessageExecutionStore`. `WaitForCompletionAsync` is useful for console applications and tests; cancelling that wait does not cancel the job.

Queue handlers may also be invoked through `InvokeAsync`, which returns acceptance rather than the eventual handler value. Prefer `EnqueueAsync` at application boundaries to make this distinction visible.

## Delivery policy

| Setting | Meaning |
| --- | --- |
| `Concurrency` | Maximum admitted deliveries per queue per process |
| `PrefetchCount` | Receive batch cap, also bounded by available concurrency |
| `TimeoutSeconds` | Renewable visibility lease, not a maximum job duration |
| `MaxAttempts` | Total attempts including the first; negative means unlimited |
| `RetryDelays` | Explicit retry schedule, such as `"1s,3s,15s"` |
| `TrackProgress` | Record execution lifecycle in the configured native store |
| `Group` | Operational worker selection |
| `QueueName` | Explicitly share one delivery and retry budget |

Successful results acknowledge. `Error`, `Unavailable`, and `RateLimited` retry. Other unsuccessful results dead-letter immediately. Exceptions retry until the budget is exhausted. Cascading messages are emitted during processing, so their side effects must also tolerate redelivery.

Foundatio controls receiving, lease renewal, and settlement. Lost ownership cancels the processing token. On shutdown, receiving stops and admitted handlers have `ShutdownTimeout` to finish. Ambiguous acknowledgments remain nonterminal in tracking and may produce another delivery.

## Progress and cancellation

Use `context.ReportProgressAsync(percent, description, ct)`. Progress updates affect only the current processing attempt. Progress also renews the delivery lease. Automatic renewal continues even when no progress is reported. `context.ReportProgressAsync(ct)` explicitly renews the lease and heartbeat.

`IMessageExecutionStore.RequestCancellationAsync(jobId, ct)` requests cooperative cancellation. A queued job is skipped before invocation; a running job observes its token. Cancellation is not a rollback of side effects. Configure shared execution tracking for hosts that exchange jobs.

With `AutoComplete = false`, await `context.CompleteAsync(ct)` or `context.AbandonAsync(delay, ct)` inside the handler. Returning without settlement leaves the delivery eligible for retry.

## Middleware and context

Use `MiddlewareStage.Enqueue` for validation and authorization, `Processing` for worker-only behavior, and `Both` when a hook should run at both boundaries. Headers cross the wire explicitly through `AddQueueHeaderProvider<T>()`; scoped services and the caller's DI container do not.

## Resource locking {#single-flight-with-queuelock}

`[QueueLock]` uses native `Foundatio.Lock.ILockProvider`. Configure `foundatio.Locking.UseRedis()` and implement `IHaveLockKey` on the message to select a resource. Distinct messages for one resource wait and run in sequence. This is mutual exclusion while the lease is owned, not deduplication or fencing of external writes.

## Administration

Mediator exposes typed dashboard queries while `MessageAdministration` and `IMessageExecutionStore` own native operations. Inspect raw dead letters before deletion or replay. Replaying creates a fresh execution identity and retains the failed history. Sending the replacement and deleting the original are not a cross-broker transaction: an uncertain replay can produce a duplicate.

SQS inspection uses bounded receive/hold/release, up to 1,000 messages or ten seconds per scan. A missing result can mean it was outside that snapshot. Shared queue retries rerun every matching handler; default independent subscriptions isolate failures.
