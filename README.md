# Foundatio.Mediator


[![NuGet](https://img.shields.io/nuget/v/Foundatio.Mediator.svg)](https://www.nuget.org/packages/Foundatio.Mediator/)

Blazingly fast, convention-based C# mediator powered by source generators and interceptors.

## ✨ Why Choose Foundatio.Mediator?

- 🚀 Near-direct call performance, zero runtime reflection
- ⚡ Convention-based handler discovery (no interfaces/base classes)
- 🔧 Full DI support via Microsoft.Extensions.DependencyInjection
- 🧩 Plain handler classes or static methods—just drop them in
- 🎪 Middleware pipeline with Before/After/Finally hooks
- 🎯 Built-in Result and Result\<T> types for rich status handling
- 🔄 Automatic cascading messages via tuple returns
- 🔒 Compile-time diagnostics and validation
- 📦 Auto-registration with no boilerplate

## 🚀 Quick Start Guide

### 1. Install the Package

```bash
dotnet add package Foundatio.Mediator
```

### 2. Register the Mediator

```csharp
services.AddMediator();
```

## 🧩 Simple Handler Example

Just add a class ending with `Handler` or `Consumer`. Methods must be named `Handle(Async)` or `Consume(Async)`. Supports multiple handler methods in a single class—for example, a `UserHandler` containing Create, Read, Update, Delete methods.

```csharp
public record Ping(string Text);

public static class PingHandler
{
    public static string Handle(Ping msg) => $"Pong: {msg.Text}";
}
```

Call it:

```csharp
var reply = mediator.Invoke<string>(new Ping("Hello"));
```

## 🔧 Dependency Injection in Handlers

Supports constructor and method injection:

```csharp
public class EmailHandler
{
    private readonly IEmailService _svc;
    public EmailHandler(IEmailService svc) => _svc = svc;

    public Task HandleAsync(SendEmail cmd, ILogger<EmailHandler> log, CancellationToken ct)
    {
        log.LogInformation("Sending to {To}", cmd.To);
        return _svc.SendAsync(cmd.To, cmd.Subject, cmd.Body, ct);
    }
}
```

## 🎪 Simple Middleware Example

Discovered by convention; static or instance with DI:

```csharp
public static class ValidationMiddleware
{
    public static HandlerResult Before(object msg)
        => MiniValidator.TryValidate(msg, out var errs)
           ? HandlerResult.Continue()
           : HandlerResult.ShortCircuit(Result.Invalid(errs));
}
```

## 📝 Logging Middleware Example

```csharp
public class LoggingMiddleware
{
    public Stopwatch Before(object msg) => Stopwatch.StartNew();

    public void Finally(object msg, Stopwatch sw, Exception? ex)
    {
        sw.Stop();
        if (ex != null)
            Console.WriteLine($"Error in {msg.GetType().Name}: {ex.Message}");
        else
            Console.WriteLine($"Handled {msg.GetType().Name} in {sw.ElapsedMilliseconds}ms");
    }
}
```

## 🎯 Using Result\<T> in Handlers

Result\<T> is our built-in discriminated union for message-oriented workflows, capturing success, validation errors, conflicts, not found states, and more—without relying on exceptions.

```csharp
public class GetUserHandler
{
    public async Task<Result<User>> HandleAsync(GetUser query) {
        var user = await _repo.Find(query.Id);
        if (user == null)
            return Result.NotFound($"User {query.Id} not found");

        // implicitly converted to Result<User>
        return user;
    }
}
```

## ➡️ Invocation API Overview

```csharp
// Async with response
var user = await mediator.InvokeAsync<User>(new GetUser(id));

// Async without response
await mediator.InvokeAsync(new Ping("Hi"));

// Sync with response (all handlers and middleware must be sync)
var reply = mediator.Invoke<string>(new Ping("Hello"));
```

## 🔄 Tuple Returns & Cascading Messages

Handlers can return tuples; one matches the response, the rest are published:

```csharp
public async Task<(User user, UserCreated evt)> HandleAsync(CreateUser cmd)
{
    var user = await _repo.Add(cmd);
    return (user, new UserCreated(user.Id));
}

// Usage
var user = await mediator.InvokeAsync<User>(new CreateUser(...));
// UserCreated is auto-published and handlers invoked inline before this method returns
```

## 📦 Publish API & Behavior

Sends a message to zero or more handlers (by convention); all are invoked inline and in parallel.
If any handler fails, `PublishAsync` throws (aggregates) exceptions.

```csharp
await mediator.PublishAsync(new OrderShipped(orderId));
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
}
```

## ⚙️ How It Works

The source generator:

1. **Discovers handlers** at compile time by scanning for classes ending with `Handler` or `Consumer`
2. **Discovers handler methods** looks for methods with names like `Handle`, `HandleAsync`, `Consume`, `ConsumeAsync`
3. **Parameters** first parameter is the message, remaining parameters are injected via DI
4. **Generates C# interceptors** for blazing fast same-assembly dispatch using direct method calls
5. **Middleware** can run `Before`, `After`, and `Finally` around handler execution and can be sync or async

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
