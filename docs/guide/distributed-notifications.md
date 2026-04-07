# Distributed Notifications

Distributed notifications broadcast events across all nodes in your cluster. When one node publishes an event, every node hears about it — without changing your publishing code or handler structure.

This is the pattern for cache invalidation, real-time state sync, configuration propagation, or any scenario where every instance of your app needs to react to the same event.

## Installation

```bash
dotnet add package Foundatio.Mediator.Distributed
```

Register the distributed notification services:

```csharp
builder.Services.AddMediator()
    .AddDistributedNotifications();
```

By default, this uses an in-memory pub/sub — useful for single-process development. For multi-node deployments, add a [transport provider](./distributed-transports).

## Making a Notification Distributed

There are several ways to mark a notification for distributed fan-out. Pick whichever fits your architecture.

### Option 1: Marker Interface

Implement `IDistributedNotification` on your event record:

```csharp
public record OrderCreated(string OrderId, string CustomerId, decimal Amount)
    : IDistributedNotification;
```

`IDistributedNotification` extends `INotification` — it's a marker interface that tells the distributed infrastructure to broadcast this event beyond the local process.

### Option 2: Attribute

When you can't or don't want to modify the type hierarchy, use the `[DistributedNotification]` attribute:

```csharp
[DistributedNotification]
public record OrderCreated(string OrderId, string CustomerId, decimal Amount);
```

This is equivalent to implementing `IDistributedNotification` — no interface needed.

### Option 3: Options-Based Configuration

For maximum flexibility, configure distribution at registration time without modifying message types at all:

```csharp
builder.Services.AddMediator()
    .AddDistributedNotifications(opts =>
    {
        // Explicitly include specific types
        opts.Include<OrderCreated>();
        opts.Include<ProductUpdated>();

        // Or include all notification types from an assembly
        opts.IncludeNotificationsFromAssemblyOf<OrderCreated>();

        // Or use a predicate for dynamic filtering
        opts.MessageFilter = type => type.Namespace?.StartsWith("MyApp.Events") == true;

        // Or distribute everything
        opts.IncludeAllNotifications = true;
    });
```

See [Controlling Which Types Are Distributed](#controlling-which-types-are-distributed) below for the full reference.

Regardless of which approach you use, your publish code stays exactly the same:

```csharp
await mediator.PublishAsync(new OrderCreated("ORD-001", "CUST-42", 99.99m));
```

## How It Works

When you publish a distributed notification:

1. **Local handlers run first** — same as any notification, all matching handlers on the publishing node execute
2. **Outbound bridge** — the `DistributedNotificationWorker` picks up the event, serializes it, and publishes it to the configured pub/sub transport
3. **Remote nodes** — each node's worker receives the message, deserializes it, and publishes it locally via `mediator.PublishAsync()`
4. **Self-loop prevention** — the originating node ignores its own broadcast, so handlers don't fire twice

```text
Node A                        Pub/Sub Bus                    Node B
  │                               │                            │
  ├─ PublishAsync(event) ─────►   │                            │
  │                               │                            │
  │  ┌─ Local Handlers ─┐        │                            │
  │  │  EmailHandler     │        │                            │
  │  │  AuditHandler     │        │                            │
  │  └──────────────────┘        │                            │
  │                               │                            │
  ├─ serialize & publish ────────►│                            │
  │                               │                            │
  │                               ├─── message ───────────────►│
  │                               │                            │
  │                               │    ┌─ Local Handlers ─┐   │
  │                               │    │  CacheHandler     │   │
  │                               │    │  DashboardHandler  │   │
  │                               │    └──────────────────┘   │
```

Each node runs its own local handlers for the event. The distributed layer just handles the transport.

## Configuration

```csharp
builder.Services.AddMediator()
    .AddDistributedNotifications(opts =>
    {
        opts.Topic = "app-events";          // Topic name (default: "distributed-notifications")
        opts.HostId = "node-1";             // Unique ID per node (default: random GUID)
        opts.ResourcePrefix = "myapp";      // Namespace prefix
        opts.MaxCapacity = 1000;            // Outbound buffer size (default: 1000)
    });
```

### Host Identity

Each node needs a unique `HostId` to prevent self-loop broadcasting. By default, a random GUID is generated — this works for most deployments. Set it explicitly when you need stable identity for debugging or monitoring:

```csharp
opts.HostId = Environment.MachineName;
// or
opts.HostId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Guid.NewGuid().ToString("N");
```

### Resource Prefixing

Use `ResourcePrefix` to namespace your topics, avoiding collisions in shared infrastructure:

```csharp
opts.ResourcePrefix = "myapp-prod";
// Topic becomes: "myapp-prod-distributed-notifications"
```

## Controlling Which Types Are Distributed {#controlling-which-types-are-distributed}

You have several mechanisms to control which notification types get distributed. They are evaluated in priority order — the first match wins:

| Priority | Mechanism | Scope |
| -------- | --------- | ----- |
| 1 | `opts.Include<T>()` | Per-type, at registration |
| 2 | `IDistributedNotification` interface | Per-type, in source |
| 3 | `[DistributedNotification]` attribute | Per-type, in source |
| 4 | `opts.MessageFilter` predicate | Dynamic, at registration |
| 5 | `opts.IncludeAllNotifications` flag | Global, at registration |

### Explicit Include

Registers specific types for distribution. Use this when the message types are defined in a library you don't control:

```csharp
opts.Include<OrderCreated>();
opts.Include<ProductUpdated>();
```

### Assembly Scanning

Includes all types implementing `INotification` in the given assembly:

```csharp
opts.IncludeNotificationsFromAssemblyOf<OrderCreated>();
```

### Custom Predicate

Filter by any criteria — namespace, naming convention, custom attributes, etc.:

```csharp
// By namespace
opts.MessageFilter = type => type.Namespace?.StartsWith("MyApp.DomainEvents") == true;

// By naming convention
opts.MessageFilter = type => type.Name.EndsWith("DomainEvent");
```

The predicate is only evaluated for types that weren't already matched by `Include<T>()`, `IDistributedNotification`, or `[DistributedNotification]`.

### Include All Notifications

Opt in to distribute every notification type. Useful for small applications or during development:

```csharp
opts.IncludeAllNotifications = true;
```

::: warning
This distributes _all_ notification types, including those that may have been intentionally local-only. Use with care in production — every published notification will be serialized and sent to the bus.
:::

### Combining Approaches

All mechanisms work together. You can use the interface for most events and `Include<T>()` for third-party types:

```csharp
// OrderCreated uses the interface
public record OrderCreated(string OrderId) : IDistributedNotification;

// ThirdPartyEvent uses explicit include
builder.Services.AddMediator()
    .AddDistributedNotifications(opts =>
    {
        opts.Include<ThirdPartyEvent>();
    });
```

### Checking the Configuration

You can verify whether a type would be distributed using `ShouldDistribute()`:

```csharp
var options = new DistributedNotificationOptions();
options.Include<OrderCreated>();

options.ShouldDistribute(typeof(OrderCreated));  // true
options.ShouldDistribute(typeof(LocalEvent));     // false
```

## Working with Handlers

Distributed notifications use the same handler conventions as local notifications. Nothing special is required:

```csharp
public class CacheInvalidationHandler
{
    public void Handle(ProductPriceChanged e, ICache cache)
    {
        cache.Remove($"product:{e.ProductId}");
    }
}

public class DashboardUpdateHandler
{
    public async Task HandleAsync(ProductPriceChanged e, IDashboardService dashboard, CancellationToken ct)
    {
        await dashboard.RefreshProductAsync(e.ProductId, ct);
    }
}
```

These handlers run on every node that receives the notification — including the originating node (where they run as normal local handlers before the event is broadcast).

## Mixing Local and Distributed Events

Not every event needs to be distributed. Only mark events for distribution when they need cross-node fanout. Regular events stay local and avoid the serialization overhead:

```csharp
// Local only — no serialization, just in-process handlers
public record OrderValidated(string OrderId);

// Distributed via interface
public record OrderCreated(string OrderId, string CustomerId) : IDistributedNotification;

// Distributed via attribute
[DistributedNotification]
public record ProductUpdated(string ProductId, decimal NewPrice);
```

Both types work with `mediator.PublishAsync()`. The distributed layer only intercepts types that match the configured distribution criteria — whether via interface, attribute, or options.

## With Queue Handlers

Distributed notifications and queues work together naturally. A common pattern is to publish a distributed event that triggers queued work:

```csharp
// Distributed event — all nodes hear about it
public record OrderCreated(string OrderId, string CustomerId, decimal Amount)
    : IDistributedNotification;

// Queue handler — processes audit logging asynchronously
[Queue(Group = "events")]
public class AuditEventHandler
{
    public async Task HandleAsync(OrderCreated e, IAuditService audit, CancellationToken ct)
    {
        await audit.LogAsync($"Order {e.OrderId} created", ct);
    }
}

// Local handler — runs on every node that receives the event
public class CacheHandler
{
    public void Handle(OrderCreated e, ICache cache)
    {
        cache.Remove("recent-orders");
    }
}
```

The same event triggers both a queued background job and a local cache invalidation on every node.

## Middleware Integration

Middleware can detect whether a message arrived via the distributed notification bus:

```csharp
[Middleware]
public class ObservabilityMiddleware
{
    public void Before(object message, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        var source = message is IDistributedNotification ? "distributed" : "local";
        logger.LogInformation("Handling {Type} (source: {Source})",
            message.GetType().Name, source);
    }
}
```

## Full Example

```csharp
// Events
public record ProductPriceChanged(string ProductId, decimal OldPrice, decimal NewPrice)
    : IDistributedNotification;

public record ProductStockChanged(string ProductId, int OldQuantity, int NewQuantity)
    : IDistributedNotification;

// Handlers — run on every node
public class ProductCacheHandler
{
    public void Handle(ProductPriceChanged e, ICache cache)
        => cache.Remove($"product:{e.ProductId}");

    public void Handle(ProductStockChanged e, ICache cache)
        => cache.Remove($"product:{e.ProductId}");
}

public class RealTimeNotificationHandler
{
    public async Task HandleAsync(ProductPriceChanged e, IHubContext<ProductHub> hub, CancellationToken ct)
    {
        await hub.Clients.All.SendAsync("PriceChanged", new
        {
            e.ProductId,
            e.NewPrice
        }, ct);
    }
}
```

```csharp
// DI registration
builder.Services.AddMediator()
    .AddDistributedNotifications(opts =>
    {
        opts.Topic = "product-events";
    });
```

```csharp
// Publishing — same as always
await mediator.PublishAsync(new ProductPriceChanged("PROD-1", 29.99m, 24.99m));
// Runs local handlers, then broadcasts to all other nodes
```
