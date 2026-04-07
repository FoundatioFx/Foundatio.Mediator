# Transport Providers

Foundatio Mediator Distributed uses pluggable transport providers for both queues and pub/sub notifications. In development, the built-in in-memory transports work out of the box. For production, swap in a real provider with a single line of configuration.

## In-Memory (Default)

The default transports require no additional packages. They're registered automatically when you call `AddDistributedQueues()` or `AddDistributedNotifications()` without a specific provider.

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddDistributedNotifications();
// Uses InMemoryQueueClient and InMemoryPubSubClient
```

::: warning Development Only
In-memory transports don't survive process restarts and don't work across multiple nodes. Use them for development and testing only.
:::

## AWS (SQS + SNS)

The AWS provider uses **SQS** for queues and **SNS + SQS** for pub/sub notifications. This is a production-grade setup that scales to millions of messages.

### Installation

```bash
dotnet add package Foundatio.Mediator.Distributed.Aws
```

### Unified Setup

The simplest way to configure both queues and notifications:

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddDistributedNotifications()
    .UseAws();
```

This uses the default AWS SDK credential chain (environment variables, IAM roles, `~/.aws/credentials`, etc.).

### With Options

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddDistributedNotifications()
    .UseAws(opts =>
    {
        // For LocalStack or custom endpoints
        opts.ServiceUrl = "http://localhost:4566";
        opts.Region = "us-east-1";

        // Queue options
        opts.Queues.AutoCreateQueues = true;     // Auto-create SQS queues (default: true)
        opts.Queues.WaitTimeSeconds = 20;        // Long-polling interval (default: 20)

        // Notification options
        opts.Notifications.AutoCreate = true;    // Auto-create SNS topics & SQS subscriptions
        opts.Notifications.TopicName = "events"; // SNS topic name
        opts.Notifications.QueuePrefix = "notifications";  // Per-node queue prefix
        opts.Notifications.WaitTimeSeconds = 20;
        opts.Notifications.CleanupOnDispose = true;  // Remove per-node queue on shutdown
    });
```

### Separate Configuration

Configure queues and notifications independently:

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddDistributedNotifications()
    .UseAwsQueues(opts =>
    {
        opts.AutoCreateQueues = true;
        opts.WaitTimeSeconds = 20;
    })
    .UseAwsNotifications(opts =>
    {
        opts.TopicName = "my-events";
        opts.AutoCreate = true;
    });
```

### How AWS Queues Work

Each `[Queue]` handler gets a dedicated SQS queue:

- **Queue name:** Message type name (or custom `QueueName`), with optional `ResourcePrefix`
- **Dead-letter queue:** `{queue}-dead-letter` — created automatically
- **Long polling:** Uses SQS long polling (`WaitTimeSeconds`) for efficient message retrieval
- **Message format:** JSON body with headers as SQS message attributes
- **Visibility timeout:** Maps to SQS visibility timeout for message locking

### How AWS Notifications Work

Distributed notifications use an **SNS topic with per-node SQS queues**:

1. One shared SNS topic for all distributed notifications
2. Each node creates its own SQS queue: `{QueuePrefix}-{HostId}`
3. The queue subscribes to the SNS topic with raw message delivery
4. Publishing sends to SNS, which fans out to all subscribed queues
5. On shutdown, the per-node queue is removed (when `CleanupOnDispose = true`)

### LocalStack for Development

Use [LocalStack](https://localstack.cloud/) to develop against AWS services locally:

```bash
# docker-compose.yml
services:
  localstack:
    image: localstack/localstack
    ports:
      - "4566:4566"
    environment:
      - SERVICES=sqs,sns
```

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddDistributedNotifications()
    .UseAws(opts => opts.ServiceUrl = "http://localhost:4566");
```

## Redis

The Redis provider currently supports **job state storage** for progress tracking. Use it alongside an AWS (or other) queue provider to persist job state across nodes.

### Installation

```bash
dotnet add package Foundatio.Mediator.Distributed.Redis
```

### Job State Store

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost"));

builder.Services.AddMediator()
    .AddDistributedQueues()
    .UseRedisJobState(opts =>
    {
        opts.KeyPrefix = "fm:jobs";                    // Redis key prefix (default: "fm:jobs")
        opts.DefaultExpiry = TimeSpan.FromHours(24);   // Job state TTL (default: 24h)
    });
```

### How Redis Job State Works

Job state is stored as Redis hashes with sorted sets for efficient querying:

- **Job record:** `{KeyPrefix}:{jobId}` — hash with all state fields
- **Queue index:** `{KeyPrefix}:queue:{queueName}` — sorted set by timestamp
- **Status index:** `{KeyPrefix}:queue:{queueName}:status:{status}` — filtered views
- **TTL:** Records expire after `DefaultExpiry` (default 24 hours)

All state transitions are atomic operations.

## Custom Providers

Implement `IQueueClient` for custom queue transports and `IPubSubClient` for custom pub/sub transports:

```csharp
public interface IQueueClient
{
    Task SendAsync(string queueName, QueueMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken ct = default);
    Task CompleteAsync(string queueName, QueueMessage message, CancellationToken ct = default);
    Task AbandonAsync(string queueName, QueueMessage message, TimeSpan? delay = null, CancellationToken ct = default);
    Task DeadLetterAsync(string queueName, QueueMessage message, string reason, CancellationToken ct = default);
    Task RenewTimeoutAsync(string queueName, QueueMessage message, TimeSpan extension, CancellationToken ct = default);
    Task EnsureQueueAsync(string queueName, CancellationToken ct = default);
}

public interface IPubSubClient
{
    Task PublishAsync(string topic, PubSubMessage message, CancellationToken ct = default);
    Task<IAsyncDisposable> SubscribeAsync(string topic, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken ct = default);
    Task EnsureTopicsAsync(IEnumerable<string> topics, CancellationToken ct = default);
}
```

Register your custom implementation before calling `AddDistributedQueues()` or `AddDistributedNotifications()`:

```csharp
builder.Services.AddSingleton<IQueueClient, MyCustomQueueClient>();
builder.Services.AddSingleton<IPubSubClient, MyCustomPubSubClient>();

builder.Services.AddMediator()
    .AddDistributedQueues()
    .AddDistributedNotifications();
```

The registration extensions skip adding the default in-memory implementations when a provider is already registered.

## Combining Providers

Mix and match providers based on your infrastructure:

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues()           // Queues via AWS SQS
    .AddDistributedNotifications()    // Notifications via AWS SNS+SQS
    .UseAws(opts =>
    {
        opts.ServiceUrl = config["AWS:ServiceURL"];
    })
    .UseRedisJobState();              // Job state via Redis
```

## Infrastructure Initialization

When using real transport providers, the distributed infrastructure needs to create queues and topics before workers start processing. This happens automatically during startup:

1. **Warmup:** Creates the first queue/topic to absorb connection overhead
2. **Batch creation:** Creates remaining infrastructure concurrently
3. **Ready signal:** Workers wait for infrastructure to be ready before polling

This is transparent — your code doesn't need to manage initialization order.
