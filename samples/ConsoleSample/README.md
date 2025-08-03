# 🚀 Foundatio.Mediator Clean Console Sample

A simplified console application demonstrating all key features of Foundatio.Mediator with minimal boilerplate.

## 🎯 What This Sample Demonstrates

### 1. Simple Command and Query Handlers

- **Static handlers** with minimal setup
- Convention-based discovery (classes ending in `Handler`)
- Simple `Handle` methods for messages

### 2. CRUD Operations with Result Pattern

- **OrderHandler** with full CRUD operations
- **`Result<T>`** pattern for success/failure handling
- Validation with detailed error messages
- Status codes (Created, NotFound, NoContent, etc.)

### 3. Event Publishing & Multiple Handlers

- **PublishAsync** for events with multiple handlers
- Event-driven architecture examples
- Audit logging and notifications

### 4. Dependency Injection

- Automatic handler registration via source generator
- Logger injection and service resolution
- Clean service configuration

### 5. Middleware Examples

- **ValidationMiddleware** (static) - Using MiniValidation for automatic validation
- **LoggingMiddleware** (instance) - Performance tracking and execution logging
- Ordered execution with `[FoundatioOrder]` attributes

## 🏗️ Project Structure

```
ConsoleSample/
├── Messages/
│   └── Messages.cs          # All message types (commands, queries, events)
├── Handlers/
│   └── Handlers.cs          # All handler implementations
├── Middleware/
│   ├── ValidationMiddleware.cs  # Static validation middleware
│   └── LoggingMiddleware.cs     # Instance logging middleware
├── Program.cs               # Application entry point
├── SampleRunner.cs          # Demo orchestration
└── ServiceConfiguration.cs  # DI setup
```

## 🔧 Key Features Shown

### Static Handlers (SimpleHandler)

```csharp
public static class SimpleHandler
{
    public static void Handle(Ping ping) { /* ... */ }
    public static string Handle(GetGreeting greeting) { /* ... */ }
}
```

### Instance Handlers with Result Pattern (OrderHandler)

```csharp
public class OrderHandler
{
    public async Task<Result<Order>> HandleAsync(CreateOrder command)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(command.CustomerId))
            return Result<Order>.Invalid(new ValidationError("CustomerId", "Customer ID is required"));

        // Business logic
        var order = new Order(/* ... */);

        // Event publishing
        await _mediator.PublishAsync(new OrderCreated(/* ... */));

        return Result<Order>.Created(order, $"/orders/{orderId}");
    }
}
```

### Event Handlers

```csharp
public class OrderNotificationHandler
{
    public void Handle(OrderCreated evt) { /* Send SMS */ }
    public void Handle(OrderUpdated evt) { /* Send update */ }
}

public class OrderAuditHandler
{
    public void Handle(OrderCreated evt) { /* Log creation */ }
    public void Handle(OrderUpdated evt) { /* Log update */ }
    public void Handle(OrderDeleted evt) { /* Log deletion */ }
}
```

### Middleware Examples

#### Static Validation Middleware

```csharp
[FoundatioOrder(1)]
public static class ValidationMiddleware
{
    public static HandlerResult Before(object message)
    {
        if (!MiniValidator.TryValidate(message, out var errors))
        {
            var validationErrors = errors.Select(kvp =>
                new ValidationError(kvp.Key, string.Join(", ", kvp.Value)))
                .ToArray();

            return HandlerResult.ShortCircuit(Result.Invalid(validationErrors));
        }

        return HandlerResult.Continue();
    }
}
```

#### Instance Logging Middleware

```csharp
[FoundatioOrder(2)]
public class LoggingMiddleware
{
    public Stopwatch Before(object message)
    {
        var stopwatch = Stopwatch.StartNew();
        return stopwatch;
    }

    public void Finally(object message, Stopwatch stopwatch, Exception? exception)
    {
        stopwatch?.Stop();

        if (exception != null)
            _logger.LogError(exception, "❌ Failed {MessageType} after {ElapsedMs}ms",
                message.GetType().Name, stopwatch?.ElapsedMilliseconds ?? 0);
        else
            _logger.LogDebug("🏁 Finished {MessageType} handler", message.GetType().Name);
    }
}
```

## 🎮 Running the Sample

```bash
dotnet run
```

This will execute all examples showing:

1. Simple ping/greeting operations
2. Complete order CRUD lifecycle
3. Event publishing with multiple handlers
4. Validation error handling

## 🧠 Source Generator Magic

The sample uses Foundatio.Mediator's source generator to:

- Auto-discover handlers by convention
- Generate efficient wrapper code
- Validate call sites at compile time
- Register handlers with DI automatically

No interfaces or base classes required!
