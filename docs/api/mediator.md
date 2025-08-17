# IMediator Interface

The `IMediator` interface is the primary entry point for sending messages in Foundatio.Mediator. It provides methods for both synchronous and asynchronous operations, as well as publishing to multiple handlers.

## Interface Definition

```csharp
public interface IMediator
{
    // Async operations
    Task InvokeAsync(object message, CancellationToken cancellationToken = default);
    Task<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default);

    // Sync operations
    void Invoke(object message, CancellationToken cancellationToken = default);
    TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default);

    // Publishing (multiple handlers)
    Task PublishAsync(object message, CancellationToken cancellationToken = default);
}
```

## Async Operations

### InvokeAsync (Fire-and-Forget)

Send a message without expecting a response:

```csharp
Task InvokeAsync(object message, CancellationToken cancellationToken = default)
```

**Usage:**
```csharp
// Send a command
await mediator.InvokeAsync(new SendEmail("user@example.com", "Hello", "World"));

// Send a notification
await mediator.InvokeAsync(new UserLoggedIn(userId, DateTime.UtcNow));
```

**Behavior:**
- Finds exactly one handler for the message type
- Throws if no handler or multiple handlers found
- Returns when handler completes successfully
- Throws if handler throws an exception

### InvokeAsync&lt;TResponse&gt; (Request-Response)

Send a message and expect a typed response:

```csharp
Task<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default)
```

**Usage:**
```csharp
// Query for data
var user = await mediator.InvokeAsync<User>(new GetUser(123));

// Command with result
var result = await mediator.InvokeAsync<Result<Order>>(new CreateOrder(...));

// Get string response
var greeting = await mediator.InvokeAsync<string>(new GetGreeting("World"));
```

**Behavior:**
- Finds exactly one handler for the message type
- Handler must return `TResponse` or compatible type
- Compile-time validation ensures return type matches
- Throws if no handler or multiple handlers found

## Synchronous Operations

### Invoke (Fire-and-Forget)

Send a message synchronously without expecting a response:

```csharp
void Invoke(object message, CancellationToken cancellationToken = default)
```

**Usage:**
```csharp
// Send a command synchronously
mediator.Invoke(new LogMessage("Something happened"));

// Process synchronously
mediator.Invoke(new ValidateData(data));
```

**Requirements:**
- All handlers and middleware in the pipeline must be synchronous
- Throws compile-time error if any async handlers/middleware exist
- Use when you need blocking execution

### Invoke&lt;TResponse&gt; (Request-Response)

Send a message synchronously and expect a typed response:

```csharp
TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default)
```

**Usage:**
```csharp
// Get data synchronously
var user = mediator.Invoke<User>(new GetUser(123));

// Calculate result
var total = mediator.Invoke<decimal>(new CalculateTotal(items));
```

**Requirements:**
- All handlers and middleware must be synchronous
- Handler must return `TResponse` or compatible type
- Compile-time validation ensures pipeline is sync-only

## Publishing

### PublishAsync (Multiple Handlers)

Send a message to zero or more handlers:

```csharp
Task PublishAsync(object message, CancellationToken cancellationToken = default)
```

**Usage:**
```csharp
// Notify all interested handlers
await mediator.PublishAsync(new OrderShipped(orderId, DateTime.UtcNow));

// Broadcast event
await mediator.PublishAsync(new SystemMaintenanceStarted());
```

**Behavior:**
- Finds all handlers for the message type (including interfaces and base classes)
- Runs all handlers **in parallel**
- Waits for all handlers to complete
- If any handler throws, collects all exceptions and throws `AggregateException`
- Returns successfully if all handlers succeed

**Handler Discovery for Publishing:**
```csharp
// All these handlers will receive OrderShipped
public class EmailHandler
{
    public Task HandleAsync(OrderShipped evt) { } // Exact type match
}

public class AuditHandler
{
    public Task HandleAsync(IOrderEvent evt) { } // Interface match
}

public class LoggingHandler
{
    public Task HandleAsync(object evt) { } // Base type match
}
```

## Registration

Register the mediator in your DI container:

```csharp
// Program.cs
services.AddMediator();

// Usage via dependency injection
public class OrderController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrderController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrder(CreateOrderRequest request)
    {
        var result = await _mediator.InvokeAsync<Result<Order>>(
            new CreateOrder(request.CustomerId, request.Amount, request.Description));

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(result.ErrorMessage);
    }
}
```

## Error Handling

### Handler Not Found

```csharp
// Throws InvalidOperationException
await mediator.InvokeAsync(new UnhandledMessage());
// "No handler found for message type 'UnhandledMessage'"
```

### Multiple Handlers (for Invoke)

```csharp
// Throws InvalidOperationException
await mediator.InvokeAsync<string>(new MessageWithMultipleHandlers());
// "Multiple handlers found for message type 'MessageWithMultipleHandlers'"
```

### Handler Exceptions

```csharp
try
{
    await mediator.InvokeAsync(new FailingCommand());
}
catch (Exception ex)
{
    // Handler exception is re-thrown as-is
    Console.WriteLine($"Handler failed: {ex.Message}");
}
```

### Publishing Exceptions

```csharp
try
{
    await mediator.PublishAsync(new EventWithFailingHandlers());
}
catch (AggregateException ex)
{
    // Multiple handler exceptions collected
    foreach (var innerEx in ex.InnerExceptions)
    {
        Console.WriteLine($"Handler failed: {innerEx.Message}");
    }
}
```

## Performance Characteristics

### Invoke vs InvokeAsync

| Method | Same Assembly | Cross Assembly | Allocations |
|--------|---------------|----------------|-------------|
| `Invoke<T>` | Direct call | DI lookup | Minimal |
| `InvokeAsync<T>` | Direct call | DI lookup | Task overhead |
| `PublishAsync` | Parallel execution | DI enumeration | Collection allocation |

### Optimization Tips

1. **Use Invoke for sync operations** when possible (no Task overhead)
2. **Prefer InvokeAsync over PublishAsync** for single handlers (better performance)
3. **Keep handlers in same assembly** when possible (interceptor performance)
4. **Minimize middleware** for hot paths (each middleware adds overhead)

## Advanced Usage

### Generic Message Handling

```csharp
// Handler can be generic
public class GenericHandler<T>
{
    public string Handle(GenericMessage<T> message)
    {
        return $"Handled {typeof(T).Name}: {message.Data}";
    }
}

// Usage
var result = await mediator.InvokeAsync<string>(new GenericMessage<User>(user));
```

### Streaming Results

```csharp
// Handler returns async enumerable
public class DataStreamHandler
{
    public async IAsyncEnumerable<DataPoint> HandleAsync(
        StreamDataRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (int i = 0; i < request.Count; i++)
        {
            yield return new DataPoint(i, DateTime.UtcNow);
            await Task.Delay(100, ct);
        }
    }
}

// Usage
await foreach (var point in mediator.Invoke<IAsyncEnumerable<DataPoint>>(
    new StreamDataRequest(10)))
{
    Console.WriteLine($"Received: {point.Value}");
}
```

### Conditional Publishing

```csharp
// Only publish if there are handlers
var handlerCount = serviceProvider.GetKeyedServices<HandlerRegistration>(
    typeof(OrderShipped).FullName).Count();

if (handlerCount > 0)
{
    await mediator.PublishAsync(new OrderShipped(orderId));
}
```

## Best Practices

1. **Use specific message types** rather than generic object parameters
2. **Prefer Result&lt;T&gt; return types** for robust error handling
3. **Use PublishAsync for events**, InvokeAsync for commands/queries
4. **Handle cancellation tokens** in long-running handlers
5. **Keep messages immutable** using records or readonly properties
6. **Test handlers independently** - they're just plain methods

## See Also

- [Result Types](../guide/result-types) - Return type patterns
- [Handler Conventions](/guide/handler-conventions) - Handler discovery rules
- [Middleware](/guide/middleware) - Cross-cutting concerns
