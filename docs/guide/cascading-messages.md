# Cascading Messages

Cascading messages is a powerful feature in Foundatio Mediator that allows handlers to automatically publish additional messages when they complete. This enables clean event-driven architectures and decoupled business logic.

## How Cascading Works

When a handler returns a tuple, the mediator performs type matching to determine which value to return to the caller and which values to publish as cascading messages:

1. **Type Matching**: The mediator finds the tuple element that matches the requested return type from `Invoke<T>`
2. **Return to Caller**: That matched element is returned to the caller
3. **Cascading Publishing**: All remaining non-null tuple elements are automatically published as messages

```csharp
public class OrderHandler
{
    public static (Result<Order>, OrderCreated, EmailNotification) Handle(CreateOrderCommand command)
    {
        var order = new Order { Id = Guid.NewGuid(), Email = command.Email };

        // Return tuple with multiple values
        return (
            Result<Order>.Created(order, $"/orders/{order.Id}"),  // Matches Invoke<Result<Order>>
            new OrderCreated(order.Id, order.Email),              // Auto-published
            new EmailNotification(order.Email, "Order Created")   // Auto-published
        );
    }
}

// Usage - Result<Order> is returned, events are published
var result = await mediator.InvokeAsync<Result<Order>>(new CreateOrderCommand("test@example.com"));
// result contains the Result<Order> from the tuple
// OrderCreated and EmailNotification are automatically published
```

### Type Matching Examples

**Exact Type Match:**

```csharp
// Handler returns (string, OrderCreated)
public static (string, OrderCreated) Handle(GetOrderStatusQuery query)
{
    return ("Processing", new OrderCreated(query.OrderId, "user@example.com"));
}

// Call with string return type
var status = await mediator.InvokeAsync<string>(new GetOrderStatusQuery("123"));
// status = "Processing", OrderCreated is published
```

**Interface/Base Class Matching:**

```csharp
// Handler returns (Result<Order>, OrderCreated)
public static (Result<Order>, OrderCreated) Handle(CreateOrderCommand command)
{
    var order = new Order();
    return (Result<Order>.Created(order), new OrderCreated(order.Id, order.Email));
}

// Can call with base Result type
var result = await mediator.InvokeAsync<Result>(new CreateOrderCommand("test@example.com"));
// Result<Order> matches Result, OrderCreated is published
```

## Tuple Return Patterns

### Basic Event Publishing

```csharp
public static (Order, OrderCreated) Handle(CreateOrderCommand command)
{
    var order = new Order { Email = command.Email };
    var orderCreated = new OrderCreated(order.Id, order.Email);

    return (order, orderCreated);  // OrderCreated will be published automatically
}
```

### Multiple Events

```csharp
public static (Order, OrderCreated, CustomerUpdated, InventoryReserved) Handle(CreateOrderCommand command)
{
    var order = new Order { Email = command.Email, ProductId = command.ProductId };

    return (
        order,                                              // Response
        new OrderCreated(order.Id, order.Email),          // Event 1
        new CustomerUpdated(command.CustomerId),           // Event 2
        new InventoryReserved(command.ProductId, 1)        // Event 3
    );
}
```

### Conditional Event Publishing

```csharp
public static (Result<Order>, OrderCreated?, CustomerWelcomeEmail?) Handle(CreateOrderCommand command)
{
    var order = new Order { Email = command.Email };

    // Only publish welcome email for new customers
    var isNewCustomer = CheckIfNewCustomer(command.Email);

    return (
        order,
        new OrderCreated(order.Id, order.Email),           // Always published
        isNewCustomer ? new CustomerWelcomeEmail(command.Email) : null  // Conditional
    );
}
```

## Real-World Example

Here's a complete e-commerce order processing example:

### Messages

```csharp
// Commands
public record CreateOrderCommand(string Email, string ProductId, int Quantity);

// Events
public record OrderCreated(string OrderId, string Email, DateTime CreatedAt);
public record InventoryReserved(string ProductId, int Quantity);
public record PaymentProcessed(string OrderId, decimal Amount);
public record CustomerNotified(string Email, string Subject, string Message);
```

### Order Handler with Cascading

```csharp
public class OrderHandler
{
    public static (Result<Order>, OrderCreated, InventoryReserved, CustomerNotified) Handle(
        CreateOrderCommand command,
        IOrderRepository repository,
        ILogger<OrderHandler> logger)
    {
        logger.LogInformation("Creating order for {Email}", command.Email);

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            Email = command.Email,
            ProductId = command.ProductId,
            Quantity = command.Quantity,
            Status = OrderStatus.Created,
            CreatedAt = DateTime.UtcNow
        };

        repository.Save(order);

        // Return response + cascade events
        return (
            Result<Order>.Created(order, $"/orders/{order.Id}"),
            new OrderCreated(order.Id, order.Email, order.CreatedAt),
            new InventoryReserved(order.ProductId, order.Quantity),
            new CustomerNotified(order.Email, "Order Confirmation", $"Order {order.Id} created")
        );
    }
}
```

### Event Handlers

Each cascaded event can have its own handlers:

```csharp
// Handle inventory reservation
public class InventoryHandler
{
    public static async Task Handle(InventoryReserved @event, IInventoryService inventory)
    {
        await inventory.ReserveAsync(@event.ProductId, @event.Quantity);
    }
}

// Handle customer notifications
public class NotificationHandler
{
    public static async Task Handle(CustomerNotified @event, IEmailService email)
    {
        await email.SendAsync(@event.Email, @event.Subject, @event.Message);
    }
}

// Handle order analytics
public class AnalyticsHandler
{
    public static async Task Handle(OrderCreated @event, IAnalyticsService analytics)
    {
        await analytics.TrackOrderCreatedAsync(@event.OrderId, @event.Email);
    }
}
```

## Complex Cascading Scenarios

### Workflow Orchestration

```csharp
public class PaymentHandler
{
    public static (Result<Payment>, PaymentProcessed?, OrderShipped?, CustomerNotified?) Handle(
        ProcessPaymentCommand command,
        IPaymentService paymentService,
        IOrderService orderService)
    {
        var payment = paymentService.ProcessPayment(command.OrderId, command.Amount);

        if (payment.IsSuccessful)
        {
            var order = orderService.MarkAsPaid(command.OrderId);

            return (
                payment,
                new PaymentProcessed(command.OrderId, command.Amount),
                order.IsReadyToShip ? new OrderShipped(command.OrderId) : null,
                new CustomerNotified(order.Email, "Payment Confirmed", "Thank you!")
            );
        }

        return (Result<Payment>.Failed("Payment failed"), null, null, null);
    }
}
```

### Saga Pattern Implementation

```csharp
public class OrderSagaHandler
{
    public static (Result, ReservationRequested?, PaymentRequested?) Handle(
        StartOrderSagaCommand command,
        ISagaRepository sagaRepo)
    {
        var saga = new OrderSaga
        {
            OrderId = command.OrderId,
            State = SagaState.Started
        };

        sagaRepo.Save(saga);

        return (
            Result.Success(),
            new ReservationRequested(command.OrderId, command.ProductId),
            new PaymentRequested(command.OrderId, command.Amount)
        );
    }

    public static (Result, OrderCompleted?) Handle(
        ReservationConfirmed @event,
        ISagaRepository sagaRepo)
    {
        var saga = sagaRepo.GetByOrderId(@event.OrderId);
        saga.MarkReservationComplete();

        if (saga.IsComplete)
        {
            return (Result.Success(), new OrderCompleted(@event.OrderId));
        }

        return (Result.Success(), null);
    }
}
```

## Advanced Patterns

### Event Sourcing Integration

```csharp
public class EventSourcedOrderHandler
{
    public static (Result<Order>, params object[]) Handle(
        CreateOrderCommand command,
        IEventStore eventStore)
    {
        var events = new List<object>
        {
            new OrderCreated(command.OrderId, command.Email),
            new InventoryReserved(command.ProductId, command.Quantity)
        };

        // Add conditional events
        if (command.IsFirstOrder)
        {
            events.Add(new FirstOrderBonus(command.Email));
        }

        var order = Order.FromEvents(events);
        eventStore.SaveEvents(command.OrderId, events);

        // Return order + all events for publishing
        return (order, events.ToArray());
    }
}
```

### Batch Processing

```csharp
public class BatchOrderHandler
{
    public static (Result<Order[]>, params object[]) Handle(
        ProcessOrderBatchCommand command,
        IOrderRepository repository)
    {
        var orders = new List<Order>();
        var events = new List<object>();

        foreach (var orderData in command.Orders)
        {
            var order = new Order(orderData);
            orders.Add(order);

            events.Add(new OrderCreated(order.Id, order.Email));
            events.Add(new InventoryReserved(order.ProductId, order.Quantity));
        }

        repository.SaveAll(orders);

        // Batch processing complete event
        events.Add(new BatchProcessingCompleted(command.BatchId, orders.Count));

        return (orders.ToArray(), events.ToArray());
    }
}
```

## Performance Considerations

### Inline vs Background Publishing

Cascaded messages are published **inline** by default, meaning:

- They execute in the same transaction/scope as the original handler
- They can affect the response time of the original request
- Failures in event handlers can impact the main operation

```csharp
// This will execute all cascaded events before returning
var result = await mediator.Invoke(new CreateOrderCommand("user@example.com"));
```

### Async Event Handlers

For better performance, make event handlers async:

```csharp
public class EmailHandler
{
    public static async Task Handle(CustomerNotified @event, IEmailService email)
    {
        // This runs asynchronously but still inline
        await email.SendAsync(@event.Email, @event.Subject, @event.Message);
    }
}
```

## Best Practices

### 1. Keep Events Small and Focused

```csharp
// Good: Focused events
public record OrderCreated(string OrderId, string Email);
public record InventoryReserved(string ProductId, int Quantity);

// Avoid: Large, multi-purpose events
public record OrderEvent(Order Order, Customer Customer, Product Product, /* ... */);
```

### 2. Use Nullable Types for Conditional Events

```csharp
public static (Order, OrderCreated, WelcomeEmail?) Handle(CreateOrderCommand command)
{
    var isNewCustomer = CheckNewCustomer(command.Email);

    return (
        order,
        new OrderCreated(order.Id),
        isNewCustomer ? new WelcomeEmail(command.Email) : null  // Conditional
    );
}
```

### 3. Limit Cascade Depth

```csharp
// Avoid deep cascading chains that are hard to follow
// A -> B -> C -> D -> E -> F  // Too deep!

// Prefer: Flat event structures
// A -> [B, C, D]  // Better
```

### 4. Handle Failures Gracefully

```csharp
public static (Result<Order>, OrderCreated?) Handle(CreateOrderCommand command)
{
    try
    {
        var order = CreateOrder(command);
        return (order, new OrderCreated(order.Id));
    }
    catch (Exception ex)
    {
        return (Result<Order>.Failed(ex.Message), null);  // No events on failure
    }
}
```

### 5. Consider Using Result Types

```csharp
public static (Result<Order>, OrderCreated?, OrderFailure?) Handle(CreateOrderCommand command)
{
    if (command.IsValid)
    {
        var order = CreateOrder(command);
        return (order, new OrderCreated(order.Id), null);
    }

    return (
        Result<Order>.Invalid("Invalid order data"),
        null,
        new OrderFailure(command.Email, "Validation failed")
    );
}
```

## Troubleshooting

### Debugging Cascaded Events

Use structured logging to track event flow:

```csharp
public class OrderHandler
{
    public static (Order, OrderCreated, InventoryReserved) Handle(
        CreateOrderCommand command,
        ILogger<OrderHandler> logger)
    {
        logger.LogInformation("Processing order creation for {Email}", command.Email);

        var events = (
            new Order(command),
            new OrderCreated(Guid.NewGuid().ToString(), command.Email),
            new InventoryReserved(command.ProductId, command.Quantity)
        );

        logger.LogInformation("Order handler will cascade {EventCount} events", 2);

        return events;
    }
}
```

### Event Handler Registration

Ensure all event handlers are discoverable:

```csharp
// Make sure event handlers follow naming conventions
public class InventoryHandler  // Ends with 'Handler'
{
    public static async Task Handle(InventoryReserved @event)  // Method named 'Handle'
    {
        // Handler implementation
    }
}
```

Cascading messages enable powerful event-driven architectures while maintaining clean, focused handler code. Use them to decouple business logic and create reactive systems that respond to domain events naturally.
