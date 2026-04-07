# Distributed Queues

Distributed queues let you offload handler execution to background workers — across processes, containers, or machines. Messages are serialized, sent to a queue, and processed asynchronously with full retry, dead-lettering, and optional progress tracking.

The best part: your handler code barely changes.

## Installation

```bash
dotnet add package Foundatio.Mediator.Distributed
```

Register the distributed queue services:

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues();
```

That's it. By default, this uses an in-memory queue — perfect for development and testing. For production, add a [transport provider](./distributed-transports).

## Making a Handler Queue-Based

Add `[Queue]` to any handler class:

```csharp
[Queue]
public class OrderProcessingHandler
{
    public async Task<Result> HandleAsync(
        ProcessOrder cmd,
        IOrderService orders,
        CancellationToken ct)
    {
        await orders.ProcessAsync(cmd, ct);
        return Result.Ok();
    }
}
```

Now when you call `mediator.InvokeAsync(new ProcessOrder(...))`, instead of running the handler inline, the message is:

1. Serialized to JSON
2. Sent to a queue named after the message type (e.g., `ProcessOrder`)
3. Returns immediately with `Result.Accepted()`

A background worker picks up the message and runs your handler — with the full middleware pipeline, DI, and error handling intact.

## How It Works

The `[Queue]` attribute injects `QueueMiddleware` into the handler's middleware pipeline. This middleware intercepts the call:

- **On the caller side:** Serializes the message, sends it to the queue, returns `Result.Accepted()`
- **On the worker side:** Deserializes the message, invokes the handler through the normal pipeline

```text
Caller                          Queue                          Worker
  │                               │                              │
  ├─ InvokeAsync(msg) ──────────►│                              │
  │                               │                              │
  ◄── Result.Accepted() ─────────┤                              │
                                  │                              │
                                  ├─── message ─────────────────►│
                                  │                              │
                                  │       ┌─ Middleware Pipeline ─┤
                                  │       │  Before hooks        │
                                  │       │  Handler.HandleAsync  │
                                  │       │  After hooks          │
                                  │       └──────────────────────┘
                                  │                              │
                                  ◄── complete / abandon ────────┤
```

## Queue Configuration

The `[Queue]` attribute accepts several configuration options:

```csharp
[Queue(
    QueueName = "custom-name",         // Default: message type name
    Concurrency = 5,                   // Concurrent consumers (default: 1)
    PrefetchCount = 10,                // Messages to prefetch (default: matches Concurrency)
    MaxAttempts = 3,                   // Total attempts: 1 initial + 2 retries (default: 3)
    TimeoutSeconds = 30,               // Visibility timeout (default: 30)
    RetryPolicy = QueueRetryPolicy.Exponential,  // Retry strategy (default: Exponential)
    RetryDelaySeconds = 5,             // Base delay between retries (default: 5)
    AutoComplete = true,               // Auto-complete on success (default: true)
    AutoRenewTimeout = true,           // Auto-extend visibility timeout (default: true)
    Group = "background-jobs",         // Worker group for selective hosting
    TrackProgress = false              // Enable job progress tracking (default: false)
)]
public class MyHandler { ... }
```

### Concurrency

Control how many messages are processed simultaneously:

```csharp
[Queue(Concurrency = 10)]
public class BulkImportHandler { ... }
```

Each worker instance runs the specified number of concurrent consumers. Scale further by running multiple worker instances.

### Retry Policies

Three retry strategies are available:

| Policy | Behavior |
| --- | --- |
| `None` | Failed messages are redelivered immediately |
| `Fixed` | Constant delay between retries |
| `Exponential` | Doubling delay with ±10% jitter (prevents thundering herd) |

```csharp
[Queue(MaxAttempts = 5, RetryPolicy = QueueRetryPolicy.Exponential, RetryDelaySeconds = 2)]
public class FlakeyApiHandler { ... }
```

With `Exponential` and `RetryDelaySeconds = 2`, retries occur at approximately 2s, 4s, 8s, 16s (with jitter).

### Result-Based Retry Decisions

The worker uses your handler's `Result` status to decide what happens next:

| Result Status | Action |
| --- | --- |
| Success, Created, Accepted, NoContent | Complete — message removed from queue |
| Error, Timeout, Unauthorized, Forbidden | Abandon — message retried up to `MaxAttempts` |
| NotFound, Invalid, CriticalError, Conflict, Gone | Dead-letter — message moved to dead-letter queue immediately |

This means your handlers can make intelligent decisions about retry-ability:

```csharp
[Queue]
public class PaymentHandler
{
    public async Task<Result> HandleAsync(ProcessPayment cmd, IPaymentGateway gateway, CancellationToken ct)
    {
        try
        {
            await gateway.ChargeAsync(cmd.Amount, cmd.CardToken, ct);
            return Result.Ok();
        }
        catch (GatewayTimeoutException)
        {
            return Result.Error("Gateway timeout — will retry");
        }
        catch (InvalidCardException)
        {
            return Result.Invalid("Card declined — no retry");
        }
    }
}
```

## Dead-Letter Queues

Messages that exceed `MaxAttempts` or return a non-retryable result are moved to a dead-letter queue named `{queue}-dead-letter`. The original message is preserved along with metadata headers:

| Header | Description |
| --- | --- |
| `fm-dead-letter-reason` | Why the message was dead-lettered |
| `fm-dead-lettered-at` | When it was dead-lettered (ISO 8601) |
| `fm-original-queue-name` | The source queue |
| `fm-dead-letter-dequeue-count` | Total processing attempts |

## QueueContext

When your handler runs inside a queue worker, a `QueueContext` is injected as a parameter. Use it for lifecycle control and progress reporting:

```csharp
[Queue(TimeoutSeconds = 60)]
public class DataImportHandler
{
    public async Task<Result> HandleAsync(
        ImportData cmd,
        QueueContext ctx,
        IImportService imports,
        CancellationToken ct)
    {
        var batches = await imports.GetBatchesAsync(cmd.FileId, ct);

        foreach (var batch in batches)
        {
            await imports.ProcessBatchAsync(batch, ct);

            // Extend the visibility timeout for long-running work
            await ctx.RenewTimeoutAsync(TimeSpan.FromSeconds(60), ct);
        }

        return Result.Ok();
    }
}
```

### Available Properties and Methods

```csharp
// Properties
ctx.QueueName          // Name of the queue
ctx.MessageType        // Type of the message being processed
ctx.DequeueCount       // How many times this message has been dequeued
ctx.MaxAttempts        // Maximum attempts before dead-lettering
ctx.EnqueuedAt         // When the message was originally enqueued
ctx.JobId              // Job ID (when TrackProgress is enabled)

// Lifecycle methods
await ctx.CompleteAsync(ct);                           // Mark as successfully processed
await ctx.AbandonAsync(delay, ct);                     // Return to queue for retry
await ctx.RenewTimeoutAsync(TimeSpan.FromSeconds(30), ct);  // Extend visibility timeout

// Progress reporting (requires TrackProgress = true)
await ctx.ReportProgressAsync(ct);                     // Heartbeat
await ctx.ReportProgressAsync(75, "Processing batch 3/4", ct);  // Percent + message
```

::: tip Auto-Complete
When `AutoComplete = true` (the default), the worker automatically completes or abandons the message based on your handler's result. You only need explicit lifecycle calls for advanced scenarios.
:::

::: tip Auto-Renew Timeout
When `AutoRenewTimeout = true` (the default), the worker automatically renews the visibility timeout at 2/3 intervals. This prevents messages from becoming visible to other consumers while your handler is still processing. You only need manual `RenewTimeoutAsync` for very long-running handlers where you want explicit control.
:::

## Progress Tracking

For long-running jobs, enable progress tracking to give callers visibility into execution status:

```csharp
[Queue(TrackProgress = true, Concurrency = 5)]
public class ReportGenerationHandler
{
    public async Task<Result> HandleAsync(
        GenerateReport cmd,
        QueueContext ctx,
        IReportService reports,
        CancellationToken ct)
    {
        var data = await reports.GatherDataAsync(cmd, ct);
        await ctx.ReportProgressAsync(25, "Data gathered", ct);

        var analysis = await reports.AnalyzeAsync(data, ct);
        await ctx.ReportProgressAsync(50, "Analysis complete", ct);

        await reports.RenderAsync(analysis, cmd.Format, ct);
        await ctx.ReportProgressAsync(75, "Report rendered", ct);

        await reports.UploadAsync(cmd.OutputPath, ct);
        return Result.Ok();
    }
}
```

### Querying Job State

When progress tracking is enabled, `InvokeAsync` returns a job ID in the accepted result. Use the `IQueueJobStateStore` to query status:

```csharp
var result = await mediator.InvokeAsync(new GenerateReport("monthly", "pdf"), ct);
var jobId = result.Value; // The job ID

// Later, check progress
var stateStore = serviceProvider.GetRequiredService<IQueueJobStateStore>();
var state = await stateStore.GetJobStateAsync(jobId, ct);

Console.WriteLine($"Status: {state.Status}");       // Queued, Processing, Completed, Failed, Cancelled
Console.WriteLine($"Progress: {state.Progress}%");   // 0-100
Console.WriteLine($"Message: {state.ProgressMessage}");
```

### Job Cancellation

Request cancellation of a tracked job:

```csharp
await stateStore.RequestCancellationAsync(jobId, ct);
```

The worker polls for cancellation and triggers the `CancellationToken` passed to your handler. Your handler should check `ct.ThrowIfCancellationRequested()` at appropriate points.

### State Store Providers

By default, job state is stored in-memory. For production multi-node setups, use a persistent store:

```csharp
// Redis (recommended for most deployments)
builder.Services.AddMediator()
    .AddDistributedQueues()
    .UseRedisJobState(opts => opts.KeyPrefix = "fm:jobs");
```

See [Transport Providers](./distributed-transports) for setup details.

## Worker Groups

In larger deployments, you may want different nodes to process different queues — API servers handle web requests while dedicated workers process background jobs:

```csharp
// Handler declares its group
[Queue(Group = "exports")]
public class ExportHandler { ... }

[Queue(Group = "imports")]
public class ImportHandler { ... }
```

```csharp
// API server — enqueue only, no workers
builder.Services.AddMediator()
    .AddDistributedQueues(opts => opts.WorkersEnabled = false);

// Export worker — only processes export queues
builder.Services.AddMediator()
    .AddDistributedQueues(opts => opts.Group = "exports");

// Import worker — only processes import queues
builder.Services.AddMediator()
    .AddDistributedQueues(opts => opts.Group = "imports");
```

You can also limit by specific queue names:

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues(opts =>
    {
        opts.Queues = new() { "ProcessOrder", "ProcessPayment" };
    });
```

## Resource Prefixing

Use `ResourcePrefix` to namespace your queues, avoiding collisions in shared infrastructure:

```csharp
builder.Services.AddMediator()
    .AddDistributedQueues(opts => opts.ResourcePrefix = "myapp-prod");
```

This prefixes all queue names: `ProcessOrder` becomes `myapp-prod-ProcessOrder`.

## Middleware Integration

Queue-processed handlers run through the same middleware pipeline as local handlers. Middleware can detect queue context:

```csharp
[Middleware]
public class ObservabilityMiddleware
{
    public Stopwatch Before(object message, HandlerExecutionInfo info, QueueContext? queueContext)
    {
        var source = queueContext is not null ? "queue" : "local";
        Log.Information("Handling {Type} (source: {Source})", message.GetType().Name, source);
        return Stopwatch.StartNew();
    }

    public void After(object message, Stopwatch sw, QueueContext? queueContext)
    {
        Log.Information("Handled in {Elapsed}ms", sw.ElapsedMilliseconds);
    }
}
```

`QueueContext` is `null` for non-queue invocations, so your middleware works naturally in both contexts.

## Full Example

Here's a complete example putting it all together:

```csharp
// Message
public record ProcessOrder(string OrderId, string CustomerId, decimal Amount);

// Handler
[Queue(
    Concurrency = 3,
    MaxAttempts = 5,
    RetryPolicy = QueueRetryPolicy.Exponential,
    TrackProgress = true,
    Group = "order-processing")]
public class OrderProcessingHandler
{
    public async Task<Result> HandleAsync(
        ProcessOrder cmd,
        QueueContext ctx,
        IOrderService orders,
        ILogger<OrderProcessingHandler> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Processing order {OrderId}, attempt {Attempt}",
            cmd.OrderId, ctx.DequeueCount);

        await ctx.ReportProgressAsync(10, "Validating order", ct);
        var validation = await orders.ValidateAsync(cmd, ct);
        if (!validation.IsValid)
            return Result.Invalid(validation.Errors);

        await ctx.ReportProgressAsync(50, "Charging payment", ct);
        var charge = await orders.ChargeAsync(cmd, ct);
        if (!charge.Success)
            return Result.Error("Payment failed — will retry");

        await ctx.ReportProgressAsync(90, "Finalizing", ct);
        await orders.FinalizeAsync(cmd, ct);

        return Result.Ok();
    }
}
```

```csharp
// DI registration
builder.Services.AddMediator()
    .AddDistributedQueues(opts =>
    {
        opts.WorkersEnabled = true;
        opts.Group = "order-processing";
    });
```

```csharp
// Enqueue from an API endpoint or another handler
var result = await mediator.InvokeAsync(new ProcessOrder("ORD-001", "CUST-42", 99.99m), ct);
// result.StatusCode == Accepted
// result.Value contains the job ID (when TrackProgress = true)
```
