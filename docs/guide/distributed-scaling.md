---
title: "Scaling Out"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 25
---

# Scaling Out

Every process runs the same build. One setting, `DistributedQueueOptions.Workers`, decides which queue workers a process runs, so growing from a single process to a fleet of independently scaled worker deployments is a configuration change.

## Worker Selection

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues(o => o.Workers = WorkerSelection.Parse(builder.Configuration["Distributed:Workers"]));
```

`WorkerSelection.Parse` accepts:

| Text | Meaning |
| --- | --- |
| `all`, empty, or missing | Every worker. The default. |
| `none` | No workers; the process only enqueues. |
| `exports,imports` | Only workers whose `[Queue(Group = ...)]` or queue name is listed. |
| `!imports` | Every worker except those listed. |
| `exports,!ProcessGridExport` | The `exports` group minus one queue. |

Names match case-insensitively against the group and the fully prefixed queue name. In code, `WorkerSelection.All`, `WorkerSelection.None`, `WorkerSelection.Only("exports")`, and `WorkerSelection.Except("imports")` do the same.

Group related handlers so they move together:

```csharp
[Queue(Group = "exports")] public class ExportDataHandler { ... }
[Queue(Group = "exports")] public class ExportGridHandler { ... }
[Queue(Group = "imports", TimeoutSeconds = 3600)] public class ImportFileHandler { ... }
[Queue(Group = "scripts", Concurrency = 8)] public class RunScriptHandler { ... }
```

## From One Process to Many

**Day one.** One deployment, `Distributed__Workers` unset. API requests and every worker run in the same process. Enqueueing still goes through the transport, so nothing changes later.

**Move the heavy group out.** Deploy a second copy of the same image with `Distributed__Workers=imports` and set the web deployment to `!imports`. The import workers now scale on their own, and a long import can no longer compete with web requests for a process.

**Split further.** One deployment per group, each with its own scaling policy: `exports`, `imports`, `scripts`, `notifications`. The web deployment runs `none`.

**One worker per process.** `Distributed__Workers=RunDataImport` runs exactly that queue. Useful for a queue that needs a large instance type or a fixed concurrency of one.

Each process registers every queue's metadata even when it runs no worker for it, so an API node can still answer administration queries for the whole topology.

## Sizing a Worker

`Concurrency` on the attribute is per process. The maximum number of active handlers is `Concurrency × replicas`; throughput also depends on handler duration, broker latency, and downstream capacity. Keep `Concurrency` at what one instance can handle and scale replicas; that is the lever an autoscaler can move.

`PrefetchCount` defaults to `Concurrency` and caps each receive. Workers request no more than their currently available concurrency, so raising prefetch above concurrency does not hold additional leased messages while they wait to execute. A transport may impose a smaller batch limit.

## Autoscaling on Queue Depth

With SQS, CloudWatch already publishes `ApproximateNumberOfMessagesVisible` and `ApproximateAgeOfOldestMessage` per queue. Queue names are stable functions of `ResourcePrefix` plus the logical handler/message subscription name or explicit `QueueName`, so alarms and scaling policies can be written once.

ECS Service Auto Scaling target tracking on a customized metric works well: divide visible messages by running task count (backlog per task) and scale to keep it near the number of messages one task processes in a minute. Set a minimum of one task for queues that must always drain, and use scale-in protection while `queue.messages.in_flight` for the process is above zero.

On Kubernetes, the KEDA `aws-sqs-queue` scaler targets the same queue length.

The library also publishes `queue.depth.visible`, `queue.depth.in_flight`, and `queue.depth.dead_letter` as OpenTelemetry gauges sampled every `QueueDepthPollInterval`; see [Operations](./distributed-operations).

## Graceful Shutdown

When the host stops:

1. The receive loop stops taking new messages immediately.
2. Messages already fetched but not yet handed to a handler are abandoned with no delay, so another worker picks them up now.
3. In-flight handlers keep running for `ShutdownTimeout` (default 30 seconds). Handlers that finish in time complete their messages normally.
4. Handlers still running when the window closes are cancelled and their messages abandoned for redelivery.

Acknowledgements use a separate bounded timeout after handler completion. A broker timeout or process failure can still leave acceptance of that acknowledgement unknown, causing redelivery. Handlers must keep their effects idempotent. Keep `ShutdownTimeout` below the host's shutdown timeout (`HostOptions.ShutdownTimeout`) and the orchestrator's stop grace period (ECS `stopTimeout`, Kubernetes `terminationGracePeriodSeconds`).

Long jobs that outlive any reasonable window should be resumable: persist progress, and expect the message to be redelivered after a deploy with `DequeueCount` incremented.

## Enqueue-Only Nodes and Provisioning

A process with `Workers = none` still needs the queues to exist. The infrastructure initializer runs on every node and, with SQS, creates or validates queues according to the transport's provisioning mode. Enqueues that arrive during provisioning wait up to `EnqueueReadyTimeout`. See [Transport Providers](./distributed-transports#provisioning) for running provisioning as a separate step with elevated permissions.
