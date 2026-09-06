---
title: "Operations"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 50
---

# Operations

Running queues in production means answering "what is stuck, and why?" and acting on it. The package ships administration handlers, metrics, and traces for that; the host decides how to expose them.

## Administration Handlers

`QueueAdministrationHandler` is a set of mediator handlers registered with the package. They have no HTTP endpoints of their own: call them from your own endpoint handler with whatever authorization your application requires.

| Message | Returns | What it does |
| --- | --- | --- |
| `GetQueueOverview` | `Result<IReadOnlyList<QueueOverview>>` | Every queue: ready, delayed, in-flight, dead letters, handlers, group, whether a worker runs in this process, 24-hour counters |
| `GetQueueDetail(queueName)` | `Result<QueueOverview>` | One queue |
| `ListQueueJobs(queueName, status, skip, take)` | `Result<IReadOnlyList<QueueJobState>>` | Tracked jobs by status, newest first |
| `GetQueueJob(jobId)` | `Result<QueueJobState>` | One tracked job, with worker identity, metadata, and last heartbeat |
| `CancelQueueJob(jobId)` | `Result<QueueJobCancellation>` | Requests cancellation; the worker honours it on its next progress report or poll |
| `ListDeadLetters(queueName, take)` | `Result<IReadOnlyList<DeadLetterView>>` | Peeks at dead letters: reason, attempts, timestamps, correlation ID, headers, body preview, and truncation indicator |
| `ReplayDeadLetters(queueName, max, messageId?)` | `Result<DeadLetterReplayResult>` | Sends dead letters back to their original queue; messages dead-lettered after the replay started are left alone |
| `PurgeDeadLetters(queueName, max)` | `Result<DeadLetterPurgeResult>` | Deletes dead letters permanently |

```csharp
[HandlerEndpointGroup("queues")]
[HandlerAuthorize(Roles = ["Admin"])]
public class QueueAdminEndpoints
{
    public Task<Result<IReadOnlyList<QueueOverview>>> HandleAsync(GetQueuesRequest _, IMediator mediator, CancellationToken ct)
        => mediator.InvokeAsync<Result<IReadOnlyList<QueueOverview>>>(new GetQueueOverview(), ct).AsTask();

    public Task<Result<DeadLetterReplayResult>> HandleAsync(ReplayRequest r, IMediator mediator, CancellationToken ct)
        => mediator.InvokeAsync<Result<DeadLetterReplayResult>>(new ReplayDeadLetters(r.QueueName, r.Max), ct).AsTask();
}
```

`DeadLetterReplayResult.Receipts` contains one `QueueReceipt` per replay. Tracked replays get a new job ID, leaving the original Failed job intact and recording `fm-original-job-id` on the message. Link the new receipt to your job detail view. Replaying does not fix the original cause of failure.

Purge deletes available dead-letter messages up to its limit; it preserves job history. Confirm the queue and limit in an operator UI, and refresh afterward: leased messages and new failures may remain. `QueueOverview.StatisticsAvailable` distinguishes unavailable transport statistics from zero counts.

Peeking and replaying receive from the dead-letter queue, so they lock the messages briefly; with SQS a peek returns them with a zero visibility timeout and polls for at most a second, so an empty listing answers in about a second.

## Metrics

Register the meter and the `queue.*` and `notifications.*` instruments flow to your OpenTelemetry pipeline:

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(DistributedMetrics.MeterName));
```

| Instrument | Type | Tags |
| --- | --- | --- |
| `queue.messages.enqueued` | counter | `queue`, `message_type`, `group` |
| `queue.messages.processed` | counter | `queue`, `message_type`, `group` |
| `queue.messages.failed` | counter | abandoned for retry |
| `queue.messages.dead_lettered` | counter | |
| `queue.messages.abandoned` | counter | `outcome` = `shutdown` or `worker-error` |
| `queue.messages.in_flight` | up-down counter | handlers running in this process |
| `queue.handler.duration` | histogram (ms) | `outcome` = `processed` or `failed` |
| `queue.depth.visible`, `queue.depth.delayed`, `queue.depth.in_flight`, `queue.depth.dead_letter` | gauges | `queue`; sampled from the transport every `QueueDepthPollInterval` |
| `notifications.published`, `notifications.received`, `notifications.dropped` | counters | `message_type`; dropped counts outbound buffer evictions |

Depth gauges are the autoscaling signal when the transport does not publish its own; with SQS prefer the CloudWatch queue metrics, which do not depend on a process being alive.

## Traces

Enqueueing starts a producer span (`Enqueue {queue}`) tagged with `messaging.destination.name` and `messaging.message.type`; the worker starts a consumer span (`Process {queue}`) that **links** to the producer rather than parenting under it, so a four-hour job does not stretch the API request's trace. Consumer spans carry `messaging.message.id`, `messaging.message.dequeue_count`, `messaging.message.conversation_id` (the correlation id), and the job id when tracked. Notification publish and process spans are tagged the same way.

Both run on the `Foundatio.Mediator` activity source.

## Job State

With `TrackProgress`, `QueueJobState` records status, progress, attempt, `WorkerId`, error, metadata, `LastUpdatedUtc`, and `LastHeartbeatUtc`. Configure `DistributedQueueOptions.WorkerId` to identify the process or replica; it defaults to machine name plus process ID. Each new attempt records its worker and resets progress. Worker identity is retained after completion for diagnosis.

A stale heartbeat warrants investigation; it can reflect worker loss or a state-store outage. It does not prove the handler stopped. Transport lease renewal is independent of state-store heartbeats, and a redelivered message updates the same job on its new attempt. Display cancellation-requested separately from Cancelled: use `IsCancellationRequestedAsync` for a pending request, and wait for worker-confirmed state before reporting completion.

Job state expires `JobStateExpiry` (default 24 hours) after its last write. The Redis store keeps nonterminal jobs for at least `NonTerminalExpiry` (default 7 days) so a live job never disappears from tracking.

## Logs

Workers log at Information when they start and stop, when a message is abandoned for redelivery during shutdown; lock contention is logged at Debug; at Warning for retryable failures, dead letters, failed renewals and state-store writes; at Error when a message cannot be dead-lettered or the receive loop fails. Message-specific worker logs include the queue name and message id.

The [Clean Architecture sample](https://github.com/FoundatioFx/Foundatio.Mediator/tree/main/samples/CleanArchitectureSample) includes a complete operations UI with status filters, job detail links, cancellation, dead-letter inspection, replay receipts, confirmed flush, and a live event feed across API and worker replicas.
