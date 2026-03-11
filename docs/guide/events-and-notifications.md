# Events & Notifications

Events are one of the most powerful features of Foundatio Mediator. They let you build **loosely coupled systems** where code reacts to things happening elsewhere — without direct references between the producer and the consumers.

## Publishing Events

Use `PublishAsync` to broadcast a message to all matching handlers:

```csharp
await mediator.PublishAsync(new OrderCreated("ORD-001", DateTime.UtcNow));
```

**By default, `PublishAsync` waits for all handlers to complete before returning.** This is a deliberate design choice — it means you can reliably add event handlers and know they will run to completion before the publishing code continues. Unlike fire-and-forget systems, you don't lose events or race against request lifetimes.

Any message type works — events don't require special interfaces:

```csharp
public record OrderCreated(string OrderId, DateTime CreatedAt);
```

## Handling Events

Any handler discovered by the source generator can handle published events. Multiple handlers can handle the same event:

```csharp
public class EmailHandler
{
    public Task HandleAsync(OrderCreated e, IEmailService email)
        => email.SendOrderConfirmationAsync(e.OrderId);
}

public class AuditHandler
{
    public void Handle(OrderCreated e, ILogger<AuditHandler> logger)
        => logger.LogInformation("Order {OrderId} created at {Time}", e.OrderId, e.CreatedAt);
}

public class InventoryHandler
{
    public Task HandleAsync(OrderCreated e, IInventoryService inventory)
        => inventory.ReserveItemsAsync(e.OrderId);
}
```

All three handlers run when `OrderCreated` is published. The publishing code doesn't know or care which handlers exist — you can add, remove, or reorder them without touching the publisher.

## The INotification Interface

`INotification` is a built-in marker interface for classifying event types:

```csharp
public record OrderCreated(string OrderId, DateTime CreatedAt) : INotification;
public record OrderShipped(string OrderId) : INotification;
```

It's completely **optional** — plain records work fine with `PublishAsync`. But it's useful for:

- **Self-documenting code** — makes it clear a type is an event, not a command or query
- **Interface subscriptions** — subscribe to all notifications with `SubscribeAsync<INotification>()`
- **Middleware filtering** — apply middleware only to notification types

You can also define your own marker interfaces for more specific grouping:

```csharp
public interface IDispatchToClient { }

public record OrderCreated(string OrderId) : INotification, IDispatchToClient;
public record ProductUpdated(string ProductId) : INotification, IDispatchToClient;
public record AuditEntry(string Action) : INotification; // Not dispatched to clients
```

## Handler Execution Order

When multiple handlers handle the same event, you can control the order they run:

```csharp
[Handler(Order = 1)]
public class ValidationHandler
{
    public void Handle(OrderCreated evt) { /* Runs first */ }
}

[Handler(Order = 2)]
public class InventoryHandler
{
    public void Handle(OrderCreated evt) { /* Runs second */ }
}

// No Order specified — runs last (default is int.MaxValue)
public class NotificationHandler
{
    public void Handle(OrderCreated evt) { /* Runs last */ }
}
```

You can also express ordering relationships without numeric values:

```csharp
[Handler(OrderBefore = [typeof(NotificationHandler)])]
public class InventoryHandler
{
    public void Handle(OrderCreated evt) { /* Runs before NotificationHandler */ }
}
```

See [Handler Conventions](./handler-conventions#handler-execution-order) for details on ordering and relative ordering.

## Publish Strategies

The default strategy (`ForeachAwait`) runs handlers sequentially and waits for each to complete. You can change this globally:

| Strategy | Behavior | Use Case |
|----------|----------|----------|
| **`ForeachAwait`** (default) | Sequential, waits for each handler | Predictable ordering, reliable completion |
| **`TaskWhenAll`** | Concurrent, waits for all to complete | Maximum throughput for independent handlers |
| **`FireAndForget`** | Concurrent, returns immediately | Background work where you don't need completion guarantees |

Configure via the assembly attribute:

```csharp
// Assembly attribute
[assembly: MediatorConfiguration(
    NotificationPublishStrategy = NotificationPublishStrategy.TaskWhenAll)]
```

::: warning
`FireAndForget` swallows exceptions and handlers may outlive the caller. Use with caution.
:::

## Error Handling

When a handler throws during `PublishAsync`, the behavior depends on the publish strategy:

- **`ForeachAwait`** — remaining handlers still execute. After all handlers complete, an `AggregateException` is thrown containing all failures.
- **`TaskWhenAll`** — all handlers run concurrently. Failures are collected and thrown as an `AggregateException`.
- **`FireAndForget`** — exceptions are swallowed.

This means a failing handler never prevents other handlers from running.

## Cascading Events

Instead of calling `PublishAsync` explicitly, handlers can return events as tuple values. The mediator automatically publishes the extra values:

```csharp
public class OrderHandler
{
    public (Result<Order>, OrderCreated?) Handle(CreateOrder command)
    {
        var order = CreateOrder(command);
        return (order, new OrderCreated(order.Id, DateTime.UtcNow));
    }
}
```

The `OrderCreated` event is published automatically after the handler returns. See [Cascading Messages](./cascading-messages) for the full API including conditional events and multi-event tuples.

## Dynamic Subscriptions

For scenarios where you need to consume events **at runtime** rather than through static handlers — such as streaming to connected clients — use `SubscribeAsync`:

```csharp
await foreach (var evt in mediator.SubscribeAsync<OrderCreated>(cancellationToken))
{
    Console.WriteLine($"Order created: {evt.OrderId}");
}
```

Subscribe to an interface to receive all matching types:

```csharp
await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(cancellationToken))
{
    // Receives OrderCreated, ProductUpdated, etc.
}
```

Each subscriber gets its own buffered channel. Configure buffer behavior with `SubscriberOptions`:

```csharp
await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(
    cancellationToken, new SubscriberOptions { MaxCapacity = 50 }))
{
    // ...
}
```

There is zero cost when nobody is subscribed — `PublishAsync` skips the subscription fan-out entirely.

For combining dynamic subscriptions with SSE streaming endpoints, see [Streaming Handlers](./streaming-handlers#dynamic-subscriptions-with-subscribeasync).

## Best Practices

- **Use `PublishAsync` for events, `InvokeAsync` for commands/queries** — events go to many handlers, commands go to exactly one
- **Keep events small and focused** — include only the data consumers need, not entire entities
- **Use nullable tuple types for conditional cascading** — `(Result<Order>, OrderCreated?)` lets you skip publishing on error paths cleanly
- **Stick with the default publish strategy** unless you have a specific reason to change it — sequential execution with guaranteed completion is the safest default for a loosely coupled system
