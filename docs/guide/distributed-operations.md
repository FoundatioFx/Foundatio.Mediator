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
| `GetQueueOverview` | `Result<IReadOnlyList<QueueOverview>>` | Every queue: depth, in-flight, dead letters, handlers, group, whether a worker runs in this process, 24-hour counters |
| `GetQueueDetail(queueName)` | `Result<QueueOverview>` | One queue |
| `ListQueueJobs(queueName, status, skip, take)` | `Result<IReadOnlyList<QueueJobState>>` | Tracked jobs by status, newest first |
| `GetQueueJob(jobId)` | `Result<QueueJobState>` | One tracked job, with metadata and last heartbeat |
| `CancelQueueJob(jobId)` | `Result<QueueJobCancellation>` | Requests cancellation; the worker honours it on its next progress report or poll |
| `ListDeadLetters(queueName, take)` | `Result<IReadOnlyList<DeadLetterView>>` | Peeks at dead letters: reason, attempts, timestamps, correlation id, body preview |
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
| `queue.depth.visible`, `queue.depth.in_flight`, `queue.depth.dead_letter` | gauges | `queue`; sampled from the transport every `QueueDepthPollInterval` |
| `notifications.published`, `notifications.received` | counters | `message_type` |

Depth gauges are the autoscaling signal when the transport does not publish its own; with SQS prefer the CloudWatch queue metrics, which do not depend on a process being alive.

## Traces

Enqueueing starts a producer span (`Enqueue {queue}`) tagged with `messaging.destination.name` and `messaging.message.type`; the worker starts a consumer span (`Process {queue}`) that **links** to the producer rather than parenting under it, so a four-hour job does not stretch the API request's trace. Consumer spans carry `messaging.message.id`, `messaging.message.dequeue_count`, `messaging.message.conversation_id` (the correlation id), and the job id when tracked. Notification publish and process spans are tagged the same way.

Both run on the `Foundatio.Mediator` activity source.

## Job State

With `TrackProgress`, a job's `QueueJobState` records status, progress, attempt, error, metadata, `LastUpdatedUtc`, and `LastHeartbeatUtc`. A `Processing` job whose heartbeat is older than a few visibility timeouts has almost certainly lost its worker; the message itself is redelivered by the transport, and the new attempt updates the same job.

Job state expires `JobStateExpiry` (default 24 hours) after its last write. The Redis store keeps `Queued` and `Processing` jobs for at least `NonTerminalExpiry` (default 7 days) so a live job never disappears from tracking.

## Logs

Workers log at Information when they start and stop, when a message is abandoned for redelivery during shutdown, and when a lock is held elsewhere; at Warning for retryable failures, dead letters, failed renewals and state-store writes; at Error when a message cannot be dead-lettered or the receive loop fails. Every worker log line carries the queue name and message id.
