---
title: "Transport Providers"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 40
---

# Transport Providers

Queues and notifications sit on pluggable transports. In development the in-memory transports work out of the box; in production you register a provider before or after `AddDistributedQueues()` and `AddDistributedNotifications()`.

## In-Memory (Default)

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddDistributedNotifications();
```

The in-memory queue models real lease semantics (received messages stay invisible until completed, abandoned, or expired) so behaviour in tests matches production, but nothing survives a restart and nothing crosses processes.

::: warning
Startup fails if workers are disabled or filtered while the in-memory queue is the default, because messages would be enqueued to a queue nothing consumes. Register a transport, or set `AllowInMemoryWithoutWorkers` in tests. Transports can be registered before or after `AddDistributedQueues()`.
:::

## AWS (SQS + SNS)

```bash
dotnet add package Foundatio.Mediator.Distributed.Aws
```

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddDistributedNotifications()
    .UseAws();
```

`UseAws()` without a `ServiceUrl` creates the SDK clients from the default credential chain and region (environment, instance or task role, profiles). Pre-registered `IAmazonSQS` and `IAmazonSimpleNotificationService` services are used when present.

```csharp
.UseAws(aws =>
{
    aws.ServiceUrl = "http://localhost:4566";   // LocalStack; static test credentials unless aws.Credentials is set
    aws.Region = "us-east-1";

    aws.Queues.Provisioning = SqsProvisioningMode.Create;   // Create | Validate | None
    aws.Queues.DeadLetterRetention = TimeSpan.FromDays(14);
    aws.Queues.WaitTimeSeconds = 20;                         // long polling

    aws.Notifications.QueuePrefix = "notifications";        // per-node subscription queues
    aws.Notifications.SubscriptionQueueRetention = TimeSpan.FromMinutes(5);
    aws.Notifications.HeartbeatInterval = TimeSpan.FromMinutes(2);
    aws.Notifications.StaleSubscriptionAge = TimeSpan.FromMinutes(10);
    aws.Notifications.CleanupOnDispose = true;
});
```

`UseAwsQueues(...)` and `UseAwsNotifications(...)` configure either half on its own.

### Queues on SQS

Each queue is an SQS standard queue named from `ResourcePrefix` plus the logical handler/message subscription name or explicit `QueueName`, with a dead-letter queue `{queue}-dead-letter`. The `[Queue]` settings become queue attributes: `TimeoutSeconds` is the SQS visibility timeout (also requested on every receive, so the transport lock and the worker's renewal cadence always agree), `MaxAttempts` sets a redrive policy whose receive count sits above it so the library's own dead-lettering runs first, and dead-letter queues keep messages for `DeadLetterRetention`.

Bodies travel as UTF-8 JSON text; headers are message attributes, or one `fm-headers` JSON attribute once there are more than the ten SQS allows. A message over 256 KB fails at enqueue naming the queue, the size, and the message type. FIFO queues are not supported; rely on idempotency and [`[QueueLock]`](./distributed-queues#single-flight-with-queuelock) rather than ordering.

### Provisioning {#provisioning}

| Mode | Behaviour |
| --- | --- |
| `Create` (default) | Missing queues and dead-letter queues are created at startup with the attributes above; existing queues with different attributes are updated. |
| `Validate` | Every queue and dead-letter queue must already exist with matching attributes. Startup fails with one exception listing every missing queue and mismatch. |
| `None` | Nothing is created or checked; queue URLs are resolved lazily. |

For deployments where application roles must not create infrastructure, provision once with elevated permissions and run the application in `Validate`:

```csharp
// e.g. from a "provision" console command
var client = (SqsQueueClient)provider.GetRequiredService<IQueueClient>();
await client.ProvisionAsync(provider.GetRequiredService<QueueTopology>().Queues
    .Select(q => new QueueDefinition { Name = q.QueueName, VisibilityTimeout = TimeSpan.FromSeconds(q.Settings.TimeoutSeconds), MaxAttempts = q.Settings.MaxAttempts })
    .ToList(), ct);
```

IAM actions by role:

| Role | Actions |
| --- | --- |
| Enqueue only | `sqs:SendMessage`, `sqs:GetQueueUrl`, `sqs:GetQueueAttributes` |
| Worker | adds `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility` |
| Notifications | adds `sqs:CreateQueue`, `sqs:DeleteQueue`, `sqs:SetQueueAttributes`, `sqs:TagQueue`, `sqs:ListQueues`, `sqs:ListQueueTags`, `sns:Subscribe`, `sns:Unsubscribe`, `sns:ListSubscriptionsByTopic`, `sns:Publish` |
| Provisioning | adds `sqs:CreateQueue`, `sqs:SetQueueAttributes`, `sqs:TagQueue`, `sns:CreateTopic`, `sns:GetTopicAttributes` |

### Notifications on SNS

One SNS topic per `ResourcePrefix` carries every distributed notification. Each process creates its own SQS subscription queue, `{QueuePrefix}-{HostId}`, subscribed with raw delivery, and deletes it on graceful shutdown when `CleanupOnDispose` is on.

Processes are not always shut down gracefully. Subscription queues therefore carry a short retention (`SubscriptionQueueRetention`), heartbeat tags refreshed every `HeartbeatInterval`, and every starting process sweeps queues under the prefix whose heartbeat is older than `StaleSubscriptionAge`, unsubscribing and deleting them. A killed task leaves nothing behind for longer than the sweep interval.

### LocalStack

```yaml
services:
  localstack:
    image: localstack/localstack:3.8.1
    ports: ["4566:4566"]
    environment:
      - SERVICES=sqs,sns
```

```csharp
.UseAws(aws => aws.ServiceUrl = "http://localhost:4566");
```

## Redis

```bash
dotnet add package Foundatio.Mediator.Distributed.Redis
```

The Redis package provides the job state store for `TrackProgress` handlers. Use it beside SQS so every node sees the same job state.

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("localhost"));

builder.Services.AddMediator()
    .AddDistributedQueues()
    .UseRedisJobState(o =>
    {
        o.KeyPrefix = "fm:jobs";                       // default
        o.ResourcePrefix = "myapp-prod";               // optional, prepended to KeyPrefix
        o.DefaultExpiry = TimeSpan.FromHours(24);      // when the caller passes no expiry
        o.NonTerminalExpiry = TimeSpan.FromDays(7);    // floor for Queued and Processing jobs
    });
```

Keys, with `{p}` = `KeyPrefix` or `{ResourcePrefix}:{KeyPrefix}`:

| Key | Contents |
| --- | --- |
| `{p}:{jobId}` | Hash: status, progress, timestamps, attempt, error, `LastHeartbeatUtc`, `meta:*` fields |
| `{p}:{jobId}:cancel` | Cancellation flag, same TTL as the job |
| `{p}:queues:{queue}` | Sorted set of jobs by creation time |
| `{p}:queues:{queue}:status:{n}` | Sorted set per status |
| `{p}:counters:{queue}:{yyyy-MM-ddTHH}` | Hourly processing counters, 48-hour TTL |

Status transitions are single MULTI/EXEC transactions conditioned on the previous status and retried on conflict, so two workers racing on one job cannot leave it in two status sets. Index sets are trimmed on write and on read, so expired jobs do not accumulate. Redis 6 is sufficient.

## Custom Providers

Implement `IQueueClient` (or derive from `QueueClientBase`) for a queue transport and `IPubSubClient` for fan-out, and register them as singletons; before or after `AddDistributedQueues()` / `AddDistributedNotifications()` both work.

```csharp
public interface IQueueClient : IAsyncDisposable
{
    Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken ct = default);
    Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken ct = default);
    Task CompleteAsync(QueueMessage message, CancellationToken ct = default);
    Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken ct = default);
    Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken ct = default);
    Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken ct = default);

    // Defaults provided; override when the transport can do better
    Task EnsureQueuesAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken ct = default);
    Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken ct = default);
    Task<IReadOnlyList<QueueMessage>> ReceiveDeadLettersAsync(string queueName, int maxCount, CancellationToken ct = default);
    Task<IReadOnlyList<QueueMessage>> ReceiveDeadLettersAsync(string queueName, int maxCount, TimeSpan waitTime, CancellationToken ct = default);
    Task ReplayAsync(QueueMessage deadLetter, CancellationToken ct = default);
}
```

A `ReceiveAsync` implementation must honour `visibilityTimeout`: the worker renews at two thirds of it, so a transport that ignores it will redeliver long-running messages early. If the transport long-polls, override the `ReceiveDeadLettersAsync` overload that takes `waitTime` and bound the poll on the server: the default cancels client-side, and a poll the server keeps running can swallow a message an administrator just released. `QueueDefinition` carries the visibility timeout, retention, and max attempts your `EnsureQueuesAsync` should apply.

## Startup

On start, an initializer creates or validates queues and topics (one warm-up call, then the rest concurrently) and then releases the workers. Enqueues arriving before it finishes wait up to `EnqueueReadyTimeout`. If provisioning fails, workers do not start and the failure is logged; enqueues fail with the same error.
