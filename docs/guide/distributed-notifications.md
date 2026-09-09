---
title: "Distributed Notifications"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 30
---

# Distributed Notifications

Use distributed notifications for best-effort signals such as cache invalidation and live UI refresh. Use queued handlers for durable business work.

```csharp
builder.Services.AddMediator()
    .AddDistributedNotifications(options => options
        .IncludeNotificationsFromAssemblyOf<OrderChanged>()
        .Exclude<InternalRefresh>());
```

Configure the native Foundatio bus separately. Include concrete types, `IDistributedNotification`, `[DistributedNotification]`, or predicates. Only registered concrete message types can be deserialized; wire headers cannot load arbitrary types or assemblies.

## Publishing

```csharp
await mediator.PublishAsync(new OrderChanged(orderId), ct);
```

Local handlers run through ordinary Mediator dispatch. The bridge uses existing typed `SubscribeAsync` streams for explicit selections and marker notifications, so unrelated local types do not enter those buffers. Overlapping selections publish each event once. Attributed types are discovered from loaded application assemblies during registration; explicitly include types from assemblies loaded later or use `MessageFilter` for dynamic discovery. Each receiving node acknowledges the native delivery before invoking its local handlers. Self-origin suppression and inbound dispatch context prevent loops and repeated queue enqueues.

`PublishAsync` does not confirm remote delivery. Each bounded subscription drops its oldest item when full. `MessageFilter` and value-type selections use a shared subscription with filtering after buffering; unrelated local traffic can fill that buffer. Broad interface selections also buffer matching excluded types before filtering. Mediator does not expose an exact drop count; this integration does not publish one. The buffers are not durable. Disconnected nodes, process termination, or subscription recovery can lose messages; consumers should refresh authoritative state.

## Native node subscriptions

The bridge calls `IMessageBus.SubscribeNodeAsync`. Memory and Redis use native expiring subscriptions; AWS uses tagged, independently owned SQS/SNS resources with heartbeat, cleanup on disposal, and stale resource cleanup at startup. Different nodes receive independent copies rather than competing on one queue.

Set `ReceiveNotifications = false` for a publisher-only host. `MaxCapacity` bounds each subscription buffer; `MaxConcurrentPublishes` bounds concurrent native publishes across all subscriptions. Setting concurrency to one serializes transport calls, but different subscription streams can interleave.

## Compose with queues

An event may have both local and queued handlers. Its originating node enqueues the durable work once per logical subscription. Other nodes run their local handlers without enqueueing that work again. By default, separate queued handlers have separate queues and retry budgets; explicit `QueueName` sharing opts into one retry unit.
