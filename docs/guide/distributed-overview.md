---
title: "Going Distributed"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 10
---

# Going Distributed

Run the [small console sample](https://github.com/FoundatioFx/Foundatio.Mediator/tree/main/samples/DistributedConsoleSample) for a complete started-host flow with validation, a typed receipt, and deterministic completion. No Docker is required.


You built your app with Foundatio Mediator. Messages flow through handlers, events trigger side effects, middleware handles cross-cutting concerns. Then the question comes: **how do we scale this out?**

The usual answer is to rip out in-process messaging and replace it with a different system: new SDKs, new serialization, new retry logic, new monitoring. Foundatio Mediator takes a different approach: **the same handlers, the same middleware, now running wherever you decide.**

## The Idea

Your handlers already don't know who calls them, and your events already don't know who listens. That is exactly the boundary a queue needs.

- **Offload work to background workers?** Add `[Queue]` to the handler.
- **Have every node hear about an event?** Mark the notification as distributed.
- **Move a set of workers to their own process so it can scale on its own?** Change one setting. No code changes.

```csharp
[Queue(Group = "exports", TrackProgress = true)]
public class ReportExportHandler
{
    public async Task<Result> HandleAsync(ExportReport cmd, QueueContext ctx, IReportService reports, CancellationToken ct)
    {
        await reports.ExportAsync(cmd.ReportId, progress => ctx.ReportProgressAsync(progress, ct: ct), ct);
        return Result.Ok();
    }
}
```

Calling `mediator.InvokeAsync(new ExportReport(...))` serializes the message, sends it to the `ExportReport` queue, and returns `Result.Accepted` to confirm transport acceptance. A worker, in this process or another, runs the handler through the normal middleware pipeline.

## One Build, Any Topology

Every process runs the same code. `DistributedQueueOptions.Workers` decides what a process does:

| Setting | Process behaviour |
| --- | --- |
| `all` (default) | API plus every worker in one process. Where you start. |
| `none` | Enqueue only. Your web nodes. |
| `exports,imports` | Only the `exports` and `imports` groups. A worker deployment you can scale independently. |
| `!imports` | Everything except the `imports` group. |

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues(o => o.Workers = WorkerSelection.Parse(builder.Configuration["Distributed:Workers"]));
```

Start with `all`. When one queue needs more capacity, deploy a second copy of the same build with `Distributed__Workers=exports` and scale that deployment on queue depth. See [Scaling Out](./distributed-scaling).

## Two Patterns, One System

**Queues** are for work that must happen, at least once, on one consumer: background jobs, order processing, report generation, imports. They give you retries, dead-lettering, visibility timeouts with automatic renewal, progress tracking, and cancellation.

**Distributed notifications** are for events every node needs to hear: cache invalidation, real-time updates, configuration changes. Publishing is unchanged; the event is broadcast and each node runs its own local handlers.

The two compose. A distributed event can have a `[Queue]` handler: the publishing node enqueues the work once, every node runs its local handlers, and the queue's worker runs the queued one.

## What Doesn't Change

| Feature | Still works |
| --- | :---: |
| Convention-based handler discovery | ✅ |
| Middleware pipeline (Before/After/Finally/Execute) | ✅ |
| Dependency injection in handlers | ✅ |
| Result types and error handling | ✅ |
| Cascading messages | ✅ |
| Handlers declared on an interface or base type | ✅ |
| OpenTelemetry tracing, plus queue metrics | ✅ |
| Authorization (enforced on the enqueuing node) | ✅ |

## Delivery Guarantees

Queues are **at least once**. A message is redelivered when a worker dies, when its handler fails with a retryable result, or when a visibility timeout lapses without renewal. Make queued handlers idempotent, or serialize them on a key with [`[QueueLock]`](./distributed-queues#single-flight-with-queuelock).

Notifications are **at most once** per node. A node that is down while an event is published does not receive it later.

## Where to Go Next

- [Distributed Queues](./distributed-queues): handlers, retries, dead letters, progress, locks, headers
- [Scaling Out](./distributed-scaling): worker selection, process topologies, autoscaling on queue depth, graceful shutdown
- [Distributed Notifications](./distributed-notifications): cross-node fan-out and which events to include
- [Transport Providers](./distributed-transports): AWS SQS/SNS, Redis job state, provisioning and IAM
- [Operations](./distributed-operations): administration handlers, dead-letter replay, metrics and traces
- [Testing](./distributed-testing): the recording queue client and multi-node tests in one process

For job tracking, use `await mediator.EnqueueAsync(message, ct)` and read the successful result’s `Value.JobId` and `Value.QueueName`. Independent queued handlers have independent subscriptions/retries by default; explicitly sharing `QueueName` opts into shared processing.
