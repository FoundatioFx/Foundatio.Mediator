# Foundatio.Mediator

**The fastest convention-based C# mediator library with source generators**

[![NuGet](https://img.shields.io/nuget/v/Foundatio.Mediator.svg)](https://www.nuget.org/packages/Foundatio.Mediator/)
[![Performance](https://img.shields.io/badge/performance-2x%20close%20to%20direct%20calls-brightgreen)](#performance-benchmarks)
[![Memory](https://img.shields.io/badge/memory-zero%20allocation-blue)](#performance-benchmarks)

Foundatio.Mediator is a high-performance, convention-based mediator library that leverages C# source generators and cutting-edge interceptors to achieve near-direct method call performance. No interfaces, no base classes, no reflection at runtime—just clean, simple code that flies.

## ✨ Why Choose Foundatio.Mediator?

- **🚀 Blazing Fast** - Nearly as fast as direct method calls, 3x faster than MediatR
- **🎯 Convention-Based** - No interfaces or base classes required
- **⚡ Source Generated** - Compile-time code generation for optimal performance
- **🔧 Full DI Integration** - Works seamlessly with Microsoft.Extensions.DependencyInjection
- **🎪 Middleware Pipeline** - Elegant middleware support
- **📦 Auto Registration** - Handlers discovered and registered automatically
- **🔒 Compile-Time Safety** - Rich diagnostics catch errors before runtime
- **🔧 C# Interceptors** - Direct method calls using cutting-edge C# interceptor technology

## 🚀 Quick Start

### 1. Install the Package

```bash
dotnet add package Foundatio.Mediator
```

### 2. Register the Mediator

```csharp
services.AddMediator();
```

### 3. Create Clean, Simple Handlers

```csharp
// Messages (any class/record)
public record PingCommand(string Id);
public record GreetingQuery(string Name);

// Handlers - just classes ending with "Handler" or "Consumer"
public class PingHandler
{
    public async Task HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Ping {command.Id} received!");
    }
}

public class GreetingHandler
{
    public string Handle(GreetingQuery query)
    {
        return $"Hello, {query.Name}!";
    }
}
```

### 4. Use the Mediator

```csharp
var mediator = serviceProvider.GetRequiredService<IMediator>();

// Fire and forget
await mediator.InvokeAsync(new PingCommand("123"));

// Request/response
var greeting = mediator.Invoke<string>(new GreetingQuery("World"));
Console.WriteLine(greeting); // "Hello, World!"
```


## 🎪 Beautiful Middleware Pipeline

Create elegant middleware that runs before, after, and finally around your handlers:

```csharp
public class LoggingMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public Stopwatch Before(object message)
    {
        _logger.LogInformation("Processing {MessageType}", message.GetType().Name);
        return Stopwatch.StartNew();
    }

    public void Finally(object message, Stopwatch stopwatch, Exception? exception)
    {
        stopwatch.Stop();
        if (exception != null)
        {
            _logger.LogError(exception, "Error processing {MessageType}", message.GetType().Name);
        }
        else
        {
            _logger.LogInformation("Completed {MessageType} in {ElapsedMs}ms",
                message.GetType().Name, stopwatch.ElapsedMilliseconds);
        }
    }
}

public class ValidationMiddleware
{
    public HandlerResult Before(object message)
    {
        if (!TryValidate(message, out var errors))
        {
            // If validation fails, short-circuit the handler execution
            return HandlerResult.ShortCircuit(Result.Invalid(errors));
        }

        return HandlerResult.Continue();
    }
}

var user = await mediator.InvokeAsync<Result<User>>(new GetUserQuery(userId), cancellationToken);
```

## 💉 Dependency Injection Made Simple

Handlers support both constructor and method-level dependency injection:

```csharp
public class SendWelcomeEmailHandler
{
    private readonly IEmailService _emailService;
    private readonly IGreetingService _greetingService;
    private readonly ILogger<SendWelcomeEmailHandler> _logger;

    public SendWelcomeEmailHandler(
        IEmailService emailService,
        IGreetingService greetingService,
        ILogger<SendWelcomeEmailHandler> logger)
    {
        _emailService = emailService;
        _greetingService = greetingService;
        _logger = logger;
    }

    public async Task HandleAsync(
        SendWelcomeEmailCommand command,
        CancellationToken cancellationToken = default) // Provided by mediator
    {
        _logger.LogInformation("Sending welcome email to {Email}", command.Email);

        var greeting = _greetingService.CreateGreeting(command.Name);
        await _emailService.SendEmailAsync(command.Email, "Welcome!", greeting);
    }
}
```

## 📊 Performance Benchmarks

Foundatio.Mediator delivers exceptional performance, getting remarkably close to direct method calls while providing full mediator pattern benefits:

### Commands (Fire-and-Forget)

| Method                        | Mean         | Error     | StdDev    | Gen0   | Allocated | vs Direct |
|-------------------------------|-------------|-----------|-----------|--------|-----------|-----------|
| **DirectPingCommandAsync**   | **8.4 ns**  | 0.18 ns   | 0.16 ns   | **-**  | **0 B**   | baseline  |
| **FoundatioPingCommandAsync** | **17.2 ns** | 0.12 ns   | 0.11 ns   | **-**  | **0 B**   | **2.05x** |
| MediatRPingCommandAsync       | 52.9 ns     | 1.00 ns   | 0.78 ns   | 0.0038 | 192 B     | 6.32x     |
| MassTransitPingCommandAsync   | 1,549.5 ns  | 19.3 ns   | 16.1 ns   | 0.0839 | 4216 B    | 185x      |

### Queries (Request/Response)

| Method                        | Mean         | Error     | StdDev    | Gen0   | Allocated | vs Direct |
|-------------------------------|-------------|-----------|-----------|--------|-----------|-----------|
| **DirectGreetingQueryAsync**  | **17.9 ns** | 0.39 ns   | 0.35 ns   | 0.0038 | **192 B** | baseline  |
| **FoundatioGreetingQueryAsync** | **31.8 ns** | 0.59 ns  | 0.66 ns   | 0.0052 | **264 B** | **1.78x** |
| MediatRGreetingQueryAsync     | 62.3 ns     | 1.27 ns   | 1.46 ns   | 0.0076 | 384 B     | 3.48x     |
| MassTransitGreetingQueryAsync | 6,192.6 ns  | 123.5 ns  | 192.2 ns  | 0.2518 | 12792 B   | 346x      |

### 🎯 Key Performance Insights

- **🚀 Near-Optimal Performance**: Only **2.05x overhead** for commands and **1.78x overhead** for queries compared to direct method calls
- **⚡ Foundatio vs MediatR**: **3.08x faster** for commands, **1.96x faster** for queries
- **� Foundatio vs MassTransit**: **90x faster** for commands, **195x faster** for queries
- **💾 Zero Allocation Commands**: Fire-and-forget operations have no GC pressure
- **🎪 Minimal Memory Overhead**: Very efficient memory usage across all scenarios

*Benchmarks run on .NET 9.0 with BenchmarkDotNet. Results show Foundatio.Mediator achieves its design goal of getting as close as possible to direct method call performance.*

## 🎯 Handler Conventions

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

## 🔧 API Reference

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

    // Publishing (multiple handlers)
    Task PublishAsync(object message, CancellationToken cancellationToken = default);
    void Publish(object message, CancellationToken cancellationToken = default);
}
```

## 🎬 Sample Applications

### ConsoleSample - Comprehensive Demonstration

The `samples/ConsoleSample` project provides a complete demonstration of all Foundatio.Mediator features:

- **Simple Commands & Queries** - Basic fire-and-forget and request/response patterns
- **Dependency Injection** - Handler methods with injected services and logging
- **Publish/Subscribe** - Multiple handlers for the same event
- **Mixed Sync/Async** - Both synchronous and asynchronous handler examples
- **Middleware Pipeline** - Global and message-specific middleware examples
- **Service Integration** - Email, SMS, and audit service examples

To run the comprehensive sample:

```bash
cd samples/ConsoleSample
dotnet run
```

## ⚙️ How It Works

The source generator:

1. **Discovers handlers** at compile time by scanning for classes ending with `Handler` or `Consumer`
2. **Discovers handler methods** looks for methods with names like `Handle`, `HandleAsync`, `Consume`, `ConsumeAsync`
3. **Parameters** first parameter is the message, remaining parameters are injected via DI
3. **Generates C# interceptors** for blazing fast same-assembly dispatch using direct method calls
4. **Middleware** with can run `Before`, `After`, and `Finally` around handler execution and can be sync or async

### 🔧 C# Interceptors - The Secret Sauce

Foundatio.Mediator uses a **dual dispatch strategy** for maximum performance and flexibility:

#### 🚀 Same-Assembly: C# Interceptors (Blazing Fast)

```csharp
// You write this:
await mediator.InvokeAsync(new PingCommand("123"));

// The source generator intercepts and transforms it to essentially this:
await PingHandler_Generated.HandleAsync(new PingCommand("123"), serviceProvider, cancellationToken);
```

#### 🌐 Cross-Assembly & Publish: DI Registration (Flexible)

```csharp
// For handlers in other assemblies or publish scenarios:
// Falls back to keyed DI registration lookup
var handlers = serviceProvider.GetKeyedServices<HandlerRegistration>("MyApp.PingCommand");
foreach (var handler in handlers)
    await handler.HandleAsync(mediator, message, cancellationToken, responseType);
```

**How the Dual Strategy Works:**

- **Interceptors First** - Same-assembly calls use interceptors for maximum performance
- **DI Fallback** - Cross-assembly handlers and publish operations use DI registration
- **Automatic Selection** - The generator chooses the optimal strategy per call site
- **Keyed Services** - Handlers registered by fully qualified message type name
- **Zero Runtime Overhead** - Interceptors bypass all runtime lookup completely

**Benefits:**

- 🚀 **Maximum Performance** - Interceptors are as fast as calling handler methods directly
- 🌐 **Cross-Assembly Support** - DI registration enables handlers across multiple projects
- 📢 **Publish Support** - Multiple handlers per message via DI enumeration
- 💾 **Zero Allocations** - Interceptors have no boxing, delegates, or intermediate objects
- 🔍 **Full IntelliSense** - All the tooling benefits of regular method calls
- 🛡️ **Type Safety** - Compile-time verification of message types and return values

The generated code is as close to direct method calls as possible, with minimal overhead.

## 🔒 Compile-Time Safety

The source generator provides compile-time errors for:

- Missing handlers for a message type
- Multiple handlers for the same message type
- Invalid handler method signatures
- Using sync methods when only async handlers exist
- Middleware configuration issues

## 📄 License

MIT License

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
