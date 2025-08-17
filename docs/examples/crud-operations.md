# CRUD Operations

This example demonstrates a complete CRUD (Create, Read, Update, Delete) implementation using Foundatio.Mediator with Result types and event publishing.

## CRUD Messages

First, let's look at the CRUD messages with validation attributes:

@[code{10-24}](../samples/ConsoleSample/Messages/Messages.cs)

## Order Model

The domain model is a simple record:

@[code{32}](../samples/ConsoleSample/Messages/Messages.cs)

## Event Messages

Events are published when operations complete:

@[code{26-28}](../samples/ConsoleSample/Messages/Messages.cs)

## CRUD Handler Implementation

Here's the complete CRUD handler with Result types and event publishing:

@[code{22-88}](../samples/ConsoleSample/Handlers/Handlers.cs)

## Key Features Demonstrated

### 1. Result&lt;T&gt; Pattern

The handlers use `Result<T>` for robust error handling:

```csharp
// Success case - implicit conversion
return order;

// Error cases - explicit Result methods
return Result.NotFound($"Order {query.OrderId} not found");
return Result<Order>.Created(order, $"/orders/{orderId}");
```

### 2. Tuple Returns & Event Publishing

Handlers return tuples where the first element is the response and additional elements are auto-published events:

```csharp
// The Order is returned to the caller
// The OrderCreated event is automatically published to all handlers
return (Result<Order>.Created(order, $"/orders/{orderId}"),
        new OrderCreated(orderId, command.CustomerId, command.Amount, order.CreatedAt));
```

### 3. Constructor Dependency Injection

The handler uses constructor injection for logging:

```csharp
public class OrderHandler
{
    private readonly ILogger<OrderHandler> _logger;

    public OrderHandler(ILogger<OrderHandler> logger)
    {
        _logger = logger;
    }
}
```

### 4. Mixed Async/Sync Methods

You can mix synchronous and asynchronous methods in the same handler:

```csharp
// Synchronous read operation
public Result<Order> Handle(GetOrder query) { }

// Asynchronous write operations
public async Task<(Result<Order> Order, OrderCreated? Event)> HandleAsync(CreateOrder command) { }
```

## Usage Examples

### Create an Order

```csharp
var createCommand = new CreateOrder(
    CustomerId: "CUST-001",
    Amount: 99.99m,
    Description: "Premium subscription"
);

var result = await mediator.InvokeAsync<Result<Order>>(createCommand);

if (result.IsSuccess)
{
    Console.WriteLine($"Order created: {result.Value.Id}");
    // OrderCreated event was automatically published
}
else
{
    Console.WriteLine($"Failed to create order: {result.ErrorMessage}");
}
```

### Get an Order

```csharp
var query = new GetOrder("ORD-20241201-1234");
var result = await mediator.InvokeAsync<Result<Order>>(query);

switch (result.Status)
{
    case ResultStatus.Success:
        Console.WriteLine($"Found order: {result.Value.Description}");
        break;
    case ResultStatus.NotFound:
        Console.WriteLine("Order not found");
        break;
    default:
        Console.WriteLine($"Error: {result.ErrorMessage}");
        break;
}
```

### Update an Order

```csharp
var updateCommand = new UpdateOrder(
    OrderId: "ORD-20241201-1234",
    Amount: 149.99m,
    Description: "Premium subscription - upgraded"
);

var result = await mediator.InvokeAsync<Result<Order>>(updateCommand);
// OrderUpdated event is automatically published on success
```

### Delete an Order

```csharp
var deleteCommand = new DeleteOrder("ORD-20241201-1234");
var result = await mediator.InvokeAsync<Result>(deleteCommand);

if (result.IsSuccess)
{
    Console.WriteLine("Order deleted successfully");
    // OrderDeleted event was automatically published
}
```

## Event Handlers

You can create separate handlers to react to the published events:

@[code{90-123}](../../../samples/ConsoleSample/Handlers/EventHandlers.cs)

## Validation Integration

The CRUD messages include validation attributes that work with validation middleware:

```csharp
public record CreateOrder(
    [Required(ErrorMessage = "Customer ID is required")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Customer ID must be between 3 and 50 characters")]
    string CustomerId,

    [Required(ErrorMessage = "Amount is required")]
    [Range(0.01, 1000000, ErrorMessage = "Amount must be between $0.01 and $1,000,000")]
    decimal Amount,

    [Required(ErrorMessage = "Description is required")]
    [StringLength(200, MinimumLength = 5, ErrorMessage = "Description must be between 5 and 200 characters")]
    string Description);
```

When used with validation middleware, invalid messages are automatically rejected before reaching the handler.

## Benefits of This Pattern

1. **Separation of Concerns**: Each operation has its own message and handler method
2. **Event-Driven**: Operations automatically publish events for other parts of the system
3. **Robust Error Handling**: Result types provide rich error information without exceptions
4. **Type Safety**: Compile-time verification of message types and return values
5. **Testability**: Each handler method can be unit tested independently
6. **Performance**: Near-direct call performance with zero reflection

## Next Steps

- [Result Types](../guide/result-types) - Deep dive into Result&lt;T&gt; usage
- [Validation Middleware](./validation-middleware) - Add validation to your handlers
