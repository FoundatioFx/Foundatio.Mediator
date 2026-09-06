---
title: "Distributed Notifications"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 30
---

# Distributed Notifications

Distributed notifications broadcast events to every node in a cluster. When one node publishes, every node hears about it, without changing publishing code or handlers. This is the pattern for cache invalidation, real-time state sync, and configuration changes.

## Installation

```csharp
builder.Services.AddMediator()
    .AddDistributedNotifications();
```

With no transport registered this uses an in-memory pub/sub, which is right for a single process. For a cluster add a [transport provider](./distributed-transports).

## Choosing What to Distribute

Only the events you choose cross the bus. Everything else stays local and costs nothing.

```csharp
// Marker interface
public record ProductPriceChanged(string ProductId, decimal NewPrice) : IDistributedNotification;

// Attribute, when the type hierarchy is not yours to change
[DistributedNotification]
public record OrderShipped(string OrderId, DateTime ShippedAt);

// Options, without touching the types at all
builder.Services.AddMediator()
    .AddDistributedNotifications(o =>
    {
        o.Include<ClientCreated>();
        o.Include<ClientUpdated>();
        o.IncludeAssignableTo<IScriptTriggerEvent>();   // every event implementing the interface
        o.Exclude<NoisyLocalEvent>();                   // never, even if a rule above matches
    });
```

Rules are evaluated in this order; the first match wins:

| Priority | Rule |
| --- | --- |
| 1 | `Exclude<T>()` (always wins) |
| 2 | `Include<T>()` |
| 3 | `IDistributedNotification` |
| 4 | `[DistributedNotification]` |
| 5 | `IncludeAssignableTo<T>()` |
| 6 | `MessageFilter` predicate |
| 7 | `IncludeAllNotifications` |

`IncludeNotificationsFromAssemblyOf<T>()` includes every concrete `INotification` in an assembly. `IncludeAllNotifications = true` distributes every notification; use it deliberately, every published notification is then serialized and sent.

The worker logs the resolved list at startup, so a deployment's bus traffic is visible in one log line.

## How It Works

1. Local handlers run first, as for any notification.
2. The outbound bridge serializes the event and publishes it to the transport with `fm-message-type`, `fm-origin-host-id`, and W3C trace headers.
3. Every other node receives it, resolves the type through its allowlist, deserializes, and publishes it locally.
4. The originating node ignores its own broadcast by host id.

```text
Node A                        Bus                          Node B
  │ PublishAsync(event)        │                            │
  │ local handlers run         │                            │
  ├─ serialize & publish ─────►├─── message ───────────────►│ local handlers run
```

A receiving node deserializes a message when its type was registered at startup, or when the type can be loaded and that node's own rules would distribute it. A handler declared on an interface therefore receives concrete events published by other nodes, and no node can be made to load a type its rules do not cover.

Delivery is best effort: events can be lost during downtime or buffer overflow, and transports may deliver duplicates. If the transport subscription fails, the node keeps publishing, logs the failure, and retries subscribing with backoff.

## With Queue Handlers

A distributed event may also have `[Queue]` handlers. The publishing node enqueues the work once; every node runs its local handlers; the queue's worker runs the queued handler. Receiving nodes do not enqueue again.

```csharp
public record OrderCreated(string OrderId) : IDistributedNotification;

[Queue(Group = "events")]
public class AuditEventHandler
{
    public Task HandleAsync(OrderCreated e, IAuditService audit, CancellationToken ct) => audit.LogAsync(e, ct);
}

public class CacheHandler
{
    public void Handle(OrderCreated e, ICache cache) => cache.Remove("recent-orders");
}
```

Commands sent from inside a notification handler are enqueued normally; only the notification itself is exempt.

## Configuration

```csharp
.AddDistributedNotifications(o =>
{
    o.Topic = "app-events";           // default "distributed-notifications"
    o.ResourcePrefix = "myapp-prod";  // topic becomes "myapp-prod-app-events"
    // HostId defaults to a unique id per process. Keep it unique among active nodes.
    o.MaxCapacity = 1000;             // waiting outbound notifications
    o.MaxConcurrentPublishes = 40;    // bounded transport publications in flight
});
```

A stable `HostId` can make SQS/SNS resources recognizable, but every concurrently running process needs a distinct value. The default generates one.

## Detecting Bus Origin in Middleware

`DistributedContext.IsNotification` is true while a node re-publishes an event it received from the bus; `DistributedContext.IsInboundNotification(message)` tells you whether a particular message is that event.

```csharp
[Middleware]
public class ObservabilityMiddleware
{
    public void Before(object message, ILogger<ObservabilityMiddleware> logger)
    {
        var source = DistributedContext.IsInboundNotification(message) ? "bus" : "local";
        logger.LogInformation("Handling {Type} from {Source}", message.GetType().Name, source);
    }
}
```

## Verifying the Configuration

```csharp
var options = new DistributedNotificationOptions().IncludeAssignableTo<IScriptTriggerEvent>();
options.ShouldDistribute(typeof(ScriptTriggered));   // true
options.ShouldDistribute(typeof(LocalOnlyEvent));    // false
```

## Delivery and startup guarantees

`AddDistributedNotifications()` also works on a publisher-only node without local handlers. Host startup waits until subscriptions are ready, so an immediate first publish is observed. The outbound buffer uses `DropOldest`: when full, the oldest waiting notification is discarded. Each discard increments `notifications.dropped` and the worker’s `DroppedCount`; warnings are rate-limited. Filtering happens before buffering, and inbound messages are not rebroadcast, including after overflow or failures.

Notifications are best effort. Awaiting `PublishAsync` does not confirm remote delivery, and shutdown can discard outstanding notifications. Use `[Queue]` subscriptions for durable independent processing. The in-memory pub/sub client logs subscriber failures and exposes `DeliveryFailed` for tests; one failed subscriber does not stop others.

The outbound worker allows up to 40 transport publications concurrently by default so broker transports can send multiple full batches. `MaxCapacity` bounds waiting notifications in addition to these in-flight publications. Set `MaxConcurrentPublishes = 1` for sequential transport publication. Concurrent publication does not preserve outbound order; SQS standard queues also provide no ordering guarantee. A publication failure is logged without stopping unrelated publications.
