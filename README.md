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
- 🐛 Superior debugging experience with short, simple call stacks

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

Just add a class (instance or static) ending with `Handler` or `Consumer`. Methods must be named `Handle(Async)` or `Consume(Async)`. First parameter is required and is always the message. Supports multiple handler methods in a single class—for example, a `UserHandler` containing handlers for all CRUD messages.

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

Just add a class (instance or static) ending with `Middleware`. Supports `Before(Async)`, `After(Async)` and `Finally(Async)` lifecycle events. First parameter is required and is always the message. Use `object` for all message types or an interface for a subset of messages. `HandlerResult` can be returned from the `Before` lifecycle method to enable short-circuiting message handling. Other return types from `Before` will be available as parameters to `After` and `Finally`.

```csharp
public static class ValidationMiddleware
{
    public static HandlerResult Before(object msg) {
        if (!TryValidate(msg, out var errors))
        {
            // short-circuit handler results when messages are invalid
            return HandlerResult.ShortCircuit(Result.Invalid(errors));
        }

        return HandlerResult.Continue();
    }
}
```

## 📝 Logging Middleware Example

```csharp
public class LoggingMiddleware(ILogger<LoggingMiddleware> log)
{
    // Stopwatch will be available as a parameter in `Finally` method
    public Stopwatch Before(object msg) => Stopwatch.StartNew();

    // Finally causes before, handler and after to be run in a try catch and is guaranteed to run
    public void Finally(object msg, Stopwatch sw, Exception? ex)
    {
        sw.Stop();
        if (ex != null)
            log.LogInformation($"Error in {msg.GetType().Name}: {ex.Message}");
        else
            log.LogInformation($"Handled {msg.GetType().Name} in {sw.ElapsedMilliseconds}ms");
    }
}
```

## 🎯 Using Result\<T> in Handlers

Result\<T> is our built-in discriminated union for message-oriented workflows, capturing success, validation errors, conflicts, not found states, and more—without relying on exceptions.

```csharp
public class UserHandler
{
    public async Task<Result<User>> HandleAsync(GetUser query) {
        var user = await _repo.Find(query.Id);
        if (user == null)
            return Result.NotFound($"User {query.Id} not found");

        // implicitly converted to Result<User>
        return user;
    }

    public async Task<Result<User>> HandleAsync(CreateUser cmd)
    {
        var user = new User {
            Id = Guid.NewGuid(),
            Name = cmd.Name,
            Email = cmd.Email,
            CreatedAt = DateTime.UtcNow
        };

        await _repo.AddAsync(user);
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

### Middleware Short-Circuit Behavior

When middleware short-circuits handler execution by returning a `HandlerResult`, the short-circuited value becomes the first tuple element, while all other tuple elements are set to their default values (null for reference types, default values for value types):

```csharp
public class ValidationMiddleware
{
    public HandlerResult Before(CreateUser cmd) {
        if (!IsValid(cmd))
        {
            var errorResult = Result.Invalid("Invalid user data");
            return HandlerResult.ShortCircuit(errorResult);
        }
        return HandlerResult.Continue();
    }
}

// If validation fails, the tuple result will be:
// (Result.Invalid("Invalid user data"), null)
// where the User is the error result and UserCreated event is null
```

## 🌊 Streaming Handler Support

Handlers can return `IAsyncEnumerable<T>` for streaming scenarios:

```csharp
public record CounterStreamRequest { }

public class StreamingHandler
{
    public async IAsyncEnumerable<int> HandleAsync(CounterStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < 10; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            await Task.Delay(1000, cancellationToken);
            yield return i;
        }
    }
}

// Usage
await foreach (var item in mediator.Invoke<IAsyncEnumerable<int>>(new CounterStreamRequest(), ct))
{
    Console.WriteLine($"Counter: {item}");
}
```

## 📦 Publish API & Behavior

Sends a message to zero or more handlers (by convention); all are invoked inline and in parallel.
If any handler fails, `PublishAsync` throws (aggregates) exceptions.

```csharp
await mediator.PublishAsync(new OrderShipped(orderId));
```

## 📊 Performance Benchmarks

Foundatio.Mediator delivers exceptional performance, getting remarkably close to direct method calls while providing full mediator pattern benefits:

### Commands

| Method                        | Mean         | Error     | StdDev    | Gen0   | Allocated | vs Direct |
|-------------------------------|-------------|-----------|-----------|--------|-----------|-----------|
| **Direct_Command**            | **8.33 ns**  | 0.17 ns   | 0.24 ns   | **-**  | **0 B**   | baseline  |
| **Foundatio_Command**         | **17.93 ns** | 0.36 ns   | 0.34 ns   | **-**  | **0 B**   | **2.15x** |
| MediatR_Command               | 54.81 ns     | 1.12 ns   | 1.77 ns   | 0.0038 | 192 B     | 6.58x     |
| MassTransit_Command           | 1,585.85 ns  | 19.82 ns  | 17.57 ns  | 0.0839 | 4232 B    | 190.4x    |

### Queries (Request/Response)

| Method                        | Mean         | Error     | StdDev    | Gen0   | Allocated | vs Direct |
|-------------------------------|-------------|-----------|-----------|--------|-----------|-----------|
| **Direct_Query**              | **32.12 ns** | 0.50 ns   | 0.47 ns   | 0.0038 | **192 B** | baseline  |
| **Foundatio_Query**           | **46.36 ns** | 0.94 ns   | 0.84 ns   | 0.0052 | **264 B** | **1.44x** |
| MediatR_Query                 | 81.40 ns     | 1.32 ns   | 1.23 ns   | 0.0076 | 384 B     | 2.53x     |
| MassTransit_Query             | 6,354.47 ns  | 125.37 ns | 195.19 ns | 0.2518 | 12784 B   | 197.8x    |

### Events (Publish/Subscribe)

| Method                        | Mean         | Error     | StdDev    | Gen0   | Allocated | vs Direct |
|-------------------------------|-------------|-----------|-----------|--------|-----------|-----------|
| **Direct_Event**              | **8.12 ns**  | 0.18 ns   | 0.36 ns   | **-**  | **0 B**   | baseline  |
| **Foundatio_Publish**         | **121.57 ns**| 0.80 ns   | 0.71 ns   | 0.0134 | **672 B** | **15.0x** |
| MediatR_Publish               | 59.29 ns     | 1.13 ns   | 1.59 ns   | 0.0057 | 288 B     | 7.30x     |
| MassTransit_Publish           | 1,697.53 ns  | 13.97 ns  | 13.06 ns  | 0.0877 | 4448 B    | 209.0x    |

### Dependency Injection Overhead

| Method                                | Mean         | Error     | StdDev    | Gen0   | Allocated | vs No DI  |
|---------------------------------------|-------------|-----------|-----------|--------|-----------|-----------|
| **Direct_QueryWithDependencies**     | **39.24 ns** | 0.81 ns   | 1.28 ns   | 0.0052 | **264 B** | baseline  |
| **Foundatio_QueryWithDependencies**  | **53.30 ns** | 1.05 ns   | 1.37 ns   | 0.0067 | **336 B** | **1.36x** |
| MediatR_QueryWithDependencies        | 79.97 ns     | 0.54 ns   | 0.51 ns   | 0.0091 | 456 B     | 2.04x     |
| MassTransit_QueryWithDependencies    | 5,397.69 ns  | 61.05 ns  | 50.98 ns  | 0.2518 | 12857 B   | 137.6x    |

### 🎯 Key Performance Insights

- **🚀 Near-Optimal Performance**: Only slight overhead vs direct method calls
- **⚡ Foundatio vs MediatR**: **3.06x faster** for commands, **1.76x faster** for queries
- **🎯 Foundatio vs MassTransit**: **88x faster** for commands, **137x faster** for queries
- **💾 Zero Allocation Commands**: Fire-and-forget operations have no GC pressure
- **🔥 Minimal DI Overhead**: Only 36% performance cost for dependency injection
- **📡 Efficient Publishing**: Event publishing scales well with multiple handlers

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
- Handler lifetime: Handlers are singleton instances by default. Register handlers in DI for custom lifetime behavior

### Ignoring Handlers

- Annotate handler classes or methods with `[FoundatioIgnore]` to exclude them from discovery

## 🎪 Middleware Conventions

- Classes should end with `Middleware`
- Valid method names:
  - `Before(...)` / `BeforeAsync(...)`
  - `After(...)` / `AfterAsync(...)`
  - `Finally(...)` / `FinallyAsync(...)`
- First parameter must be the message (can be `object`, an interface, or a concrete type)
- Lifecycle methods are optional—you can implement any subset (`Before`, `After`, `Finally`)
- `Before` can return:
  - a `HandlerResult` to short-circuit execution
  - a single state value
  - a tuple of state values
- Values (single or tuple elements) returned from `Before` are matched by type and injected into `After`/`Finally` parameters
- `After` runs only on successful handler completion
- `Finally` always runs, regardless of success or failure
- Methods may declare additional parameters: `CancellationToken`, DI-resolved services

### Ignoring Middleware

- Annotate middleware classes or methods with `[FoundatioIgnore]` to exclude them from discovery

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
6. **Handler lifetime** handlers are singleton instances by default (not registered in DI). Register handlers in DI for custom lifetime behavior

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

## 📋 Remaining Work

- [ ] Simplify tests to use Roslyn source generator testing utilities and have it generate code in memory and do asserts there instead of having all integration tests

## 📄 License

MIT License

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
