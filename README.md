# Foundatio.Mediator

A fast, convention-based C# mediator library using incremental source generators.

## Features

- **Convention-based handler discovery** - No interfaces or base classes required
- **Compile-time code generation** - Uses incremental source generators for optimal performance
- **Full DI integration** - Works seamlessly with Microsoft.Extensions.DependencyInjection
- **Automatic handler registration** - Handlers are automatically discovered and registered
- **Multiple invocation patterns** - Supports both sync and async operations with optional return values

## Quick Start

### 1. Install the Package

```bash
dotnet add package Foundatio.Mediator
```

### 2. Register the Mediator

```csharp
services.AddMediator();
```

### 3. Create Messages and Handlers

```csharp
// Messages (any class/record)
public record PingCommand;
public record GreetingQuery(string Name);

// Handlers (classes ending with "Handler" or "Consumer")
public class PingHandler
{
    public async Task HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Ping received!");
    }
}

public class GreetingHandler
{
    public async Task<string> HandleAsync(GreetingQuery query, CancellationToken cancellationToken = default)
    {
        return $"Hello, {query.Name}!";
    }
}
```

### 4. Use the Mediator

```csharp
var mediator = serviceProvider.GetRequiredService<IMediator>();

// Fire and forget
await mediator.InvokeAsync(new PingCommand());

// Request/response
var greeting = await mediator.InvokeAsync<string>(new GreetingQuery("World"));
Console.WriteLine(greeting); // "Hello, World!"
```

## Samples

### ConsoleSample - Comprehensive Demonstration

The `samples/ConsoleSample` project provides a complete demonstration of all Foundatio.Mediator features:

- **Simple Commands & Queries** - Basic fire-and-forget and request/response patterns
- **Dependency Injection** - Handler methods with injected services and logging  
- **Publish/Subscribe** - Multiple handlers for the same event
- **Mixed Sync/Async** - Both synchronous and asynchronous handler examples
- **Service Integration** - Email, SMS, and audit service examples

To run the comprehensive sample:

```bash
cd samples/ConsoleSample
dotnet run
```

This sample consolidates functionality that demonstrates the full capabilities of the mediator library in a single, easy-to-follow application.

## Handler Conventions

### Class Names
Handler classes must end with:
- `Handler` 
- `Consumer`

### Method Names
Valid handler method names:
- `Handle` / `HandleAsync`
- `Handles` / `HandlesAsync` 
- `Consume` / `ConsumeAsync`
- `Consumes` / `ConsumesAsync`

### Method Signatures

- First parameter: the message object
- Remaining parameters: injected via DI (including `CancellationToken`)
- Return type: any type (including `void`, `Task`, `Task<T>`)

**Dependency Injection Support:**
- Constructor injection: Handler classes support full constructor DI
- Method injection: Handler methods can declare any dependencies as parameters
- Known parameters: `CancellationToken` is automatically provided by the mediator
- Service resolution: All other parameters are resolved from the DI container

### Example with Dependency Injection

```csharp
public class SendEmailHandler
{
    public async Task HandleAsync(
        SendEmailCommand command,
        IEmailService emailService,        // Injected from DI
        ILogger<SendEmailHandler> logger,  // Injected from DI
        CancellationToken cancellationToken = default) // Provided by mediator
    {
        logger.LogInformation("Sending email to {Email}", command.Email);
        await emailService.SendAsync(command.Email, command.Subject, command.Body);
    }
}
```

## API Reference

### IMediator Interface

```csharp
public interface IMediator
{
    // Async operations
    Task InvokeAsync(object message, CancellationToken cancellationToken = default);
    Task<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default);
    
    // Sync operations  
    void Invoke(object message, CancellationToken cancellationToken = default);
    TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default);
    
    // Publishing (future feature)
    Task PublishAsync(object message, CancellationToken cancellationToken = default);
    void Publish(object message, CancellationToken cancellationToken = default);
}
```

## How It Works

The source generator:

1. **Discovers handlers** at compile time by scanning for classes ending with "Handler" or "Consumer"
2. **Validates method signatures** to ensure they follow the conventions
3. **Generates a mediator implementation** with optimized dispatch logic using switch expressions
4. **Registers handlers automatically** in the DI container

The generated code is as close to direct method calls as possible, with minimal overhead.

## Compile-Time Safety

The source generator provides compile-time errors for:
- Missing handlers for a message type
- Multiple handlers for the same message type  
- Invalid handler method signatures
- Using sync methods when only async handlers exist

## Roadmap

- [ ] Multiple handler support (for publish scenarios)
- [ ] Middleware pipeline support
- [ ] Tuple return values with cascading messages
- [ ] Cross-assembly handler discovery
- [ ] Source interceptors for call-site optimization
- [ ] Performance benchmarks vs other mediator libraries

## Performance

Generated code uses switch expressions and direct method calls, resulting in performance that's very close to calling handler methods directly. No reflection or dynamic dispatch at runtime.

## License

MIT License
