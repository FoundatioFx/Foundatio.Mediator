# Going Distributed

You've built your app with Foundatio Mediator. Messages flow through handlers, events trigger side effects, middleware handles cross-cutting concerns. Everything is loosely coupled, easy to test, and a joy to work with. Then the question comes:

**"How do we scale this out?"**

Traditionally, the answer is: rip out your in-process messaging and replace it with a completely different system — RabbitMQ, Kafka, SQS, whatever. New SDKs, new serialization, new error handling, new retry logic, new monitoring. Your beautiful mediator-based architecture gets buried under infrastructure plumbing.

Foundatio Mediator takes a different approach: **the same messaging system you already love, now distributed.**

## The Idea

You've already invested in a loosely coupled, message-driven architecture. Your handlers don't know who calls them. Your events don't know who listens. That's _exactly_ the abstraction boundary you need -- the handlers don't care if the message came from the same process or from a queue on the other side of the world.

- **Need to offload work to background workers?** Add `[Queue]` to the handler. Done.
- **Need all nodes in your cluster to hear about an event?** Implement `IDistributedNotification`, add `[DistributedNotification]`, or configure it in options. Done.

Your handler code doesn't change. Your tests don't change. Your middleware still runs. You're just telling the infrastructure _where_ to execute, not _how_.

## Two Patterns, One System

Foundatio Mediator Distributed provides two complementary patterns:

### Queues — Offload and Scale Out

Queues are for **work that needs to happen exactly once**, processed by one consumer. Think background jobs, order processing, report generation, data imports.

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

When you call `mediator.InvokeAsync(new ProcessOrder(...))`, the message is serialized, sent to a queue, and returns immediately with `Result.Accepted()`. A worker picks it up and runs your handler — with full middleware, DI, and error handling.

### Distributed Notifications — Broadcast Across Nodes

Distributed notifications are for **events that every node needs to hear about**. Think cache invalidation, real-time updates, configuration changes.

```csharp
// Option 1: Marker interface
public record ProductPriceChanged(string ProductId, decimal NewPrice) : IDistributedNotification;

// Option 2: Attribute (no interface needed)
[DistributedNotification]
public record OrderShipped(string OrderId, DateTime ShippedAt);

// Option 3: Configure in options (no changes to the type at all)
opts.Include<ExternalLibraryEvent>();
```

When you call `mediator.PublishAsync(new ProductPriceChanged(...))`:
1. Local handlers on the publishing node run immediately (as usual)
2. The event is automatically broadcast to all other nodes in the cluster
3. Remote nodes receive the event and run their own local handlers

No code changes. No extra publish calls. Just mark which events to distribute.

## What Doesn't Change

This is important: **going distributed doesn't change your programming model.** Everything you already know about Foundatio Mediator still applies:

| Feature | Still works? |
|---------|:---:|
| Convention-based handler discovery | ✅ |
| Middleware pipeline (Before/After/Finally) | ✅ |
| Dependency injection in handlers | ✅ |
| Result types and error handling | ✅ |
| Cascading messages | ✅ |
| Source generator optimizations | ✅ |
| OpenTelemetry tracing | ✅ |
| Authorization | ✅ |

The distributed layer wraps your existing handlers — it doesn't replace them.

## When to Use What

| Scenario | Pattern | Why |
|----------|---------|-----|
| Background job processing | `[Queue]` | Offload long-running work |
| Scaling out CPU-intensive work | `[Queue]` with `Concurrency` | Multiple workers across nodes |
| At-least-once delivery guarantee | `[Queue]` | Built-in retry and dead-lettering |
| Cache invalidation across nodes | Distributed notification | All nodes need to react |
| Real-time event broadcast | Distributed notification | Fan-out to entire cluster |
| Progress tracking for long jobs | `[Queue(TrackProgress = true)]` | Job status and progress reporting |
| You don't need distribution at all | Neither | Just use Foundatio Mediator as-is |

## Getting Started

Ready to go distributed? Start with the specific guide for your scenario:

- **[Distributed Queues](./distributed-queues)** — Background processing, retries, dead-lettering, progress tracking
- **[Distributed Notifications](./distributed-notifications)** — Cross-node event broadcasting
- **[Transport Providers](./distributed-transports)** — AWS SQS/SNS, Redis, and custom providers
