# Simple Handlers

This page demonstrates the basic handler patterns using real examples from the Foundatio.Mediator sample project.

## Basic Message and Handler

Here's how to create simple messages and handlers:

### Messages

Messages are simple record types that carry data:

<<< ../../samples/ConsoleSample/Messages/Messages.cs#Simple{c#}

### Static Handlers

The simplest handlers are static methods:

@[code{9-20}](../samples/ConsoleSample/Handlers/Handlers.cs)

These handlers follow the naming conventions:

- Class name ends with `Handler`
- Method name is `Handle` or `HandleAsync`
- First parameter is the message type

## Usage Examples

### Fire-and-Forget Commands

For commands that don't return a value:

```csharp
// Send a ping message (no response expected)
await mediator.InvokeAsync(new Ping("Hello World"));
```

### Request-Response Queries

For queries that return data:

```csharp
// Get a greeting (returns a string)
var greeting = await mediator.InvokeAsync<string>(new GetGreeting("World"));
Console.WriteLine(greeting); // Output: "Hello, World!"
```

### Synchronous Operations

When all handlers and middleware are synchronous, you can use the sync API:

```csharp
// Synchronous call (no async/await needed)
var greeting = mediator.Invoke<string>(new GetGreeting("World"));
```

## Handler Method Variations

### Async Handlers

For handlers that need to perform async operations:

```csharp
public static class AsyncHandler
{
    public static async Task<string> HandleAsync(GetGreeting greeting)
    {
        // Simulate async work
        await Task.Delay(10);
        return $"Hello, {greeting.Name}!";
    }
}
```

### Void Handlers

For commands that don't need to return anything:

```csharp
public static class NotificationHandler
{
    public static void Handle(Ping ping)
    {
        Console.WriteLine($"Received: {ping.Text}");
    }

    public static async Task HandleAsync(Ping ping)
    {
        await SomeAsyncOperation();
        Console.WriteLine($"Processed: {ping.Text}");
    }
}
```

## Instance-Based Handlers

You can also use instance-based handlers for dependency injection:

```csharp
public class UserHandler
{
    private readonly IUserRepository _repository;

    public UserHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async Task<User> HandleAsync(GetUser query)
    {
        return await _repository.GetByIdAsync(query.Id);
    }
}
```

## Key Points

1. **Convention-Based**: No interfaces or base classes required
2. **Flexible**: Use static or instance methods as needed
3. **Type-Safe**: Strong typing with compile-time validation
4. **Performance**: Near-direct call performance with interceptors
5. **Simple**: Minimal boilerplate and setup

## Next Steps

- [CRUD Operations](./crud-operations) - More complex handler examples
- [Handler Conventions](../guide/handler-conventions) - Complete convention rules
