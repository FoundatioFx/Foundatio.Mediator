# Performance & Interceptors

Foundatio Mediator achieves blazing fast performance through C# interceptors and source generators, eliminating runtime reflection and providing near-direct call performance.

## How Interceptors Work

C# interceptors are a compile-time feature that allows the mediator to replace method calls with direct, static method calls. This eliminates the overhead of traditional mediator patterns.

### Traditional Mediator Call Flow

```text
Your Code → IMediator.Send() → Reflection → Handler Discovery → Handler Instantiation → Method Invoke
```

### Foundatio Mediator with Interceptors

```text
Your Code → [Intercepted] → Direct Static Method Call → Handler Method
```

## Interceptor Generation

The source generator automatically creates interceptor wrappers for handlers:

### Your Handler

```csharp
public class OrderHandler
{
    public static Result<Order> Handle(CreateOrderCommand command)
    {
        return new Order { Id = Guid.NewGuid(), Email = command.Email };
    }
}
```

### Generated Interceptor (Simplified)

```csharp
// Generated at compile time
file static class GeneratedInterceptors
{
    [InterceptsLocation("Program.cs", 15, 42)] // Intercepts specific call site
    public static Result<Order> InterceptCreateOrder(this IMediator mediator, CreateOrderCommand command)
    {
        // Direct call - no reflection!
        return OrderHandler.Handle(command);
    }
}
```

### Your Code

```csharp
// This call gets intercepted at compile time
var result = await mediator.Invoke(new CreateOrderCommand("user@example.com"));
```

## Performance

> 📊 **Benchmark Environment**: .NET 10.0 with BenchmarkDotNet
> 🎯 **Goal**: Get as close as possible to direct method call performance

### Commands

**Fire-and-forget operations with zero allocations**

| Implementation | Execution Time | Error | Memory Allocated | Performance vs Direct |
|:---------------|---------------:|------:|-----------------:|----------------------:|
| 🏆 **Direct Method Call** | **8.33 ns** | ±0.17 ns | **0 B** | **Baseline** |
| ⚡ **Foundatio Mediator** | **17.93 ns** | ±0.36 ns | **0 B** | **2.15x slower** |
| 🔶 MediatR | 54.81 ns | ±1.12 ns | 192 B | 6.58x slower |
| 🔴 MassTransit | 1,585.85 ns | ±19.82 ns | 4,232 B | 190.4x slower |

### Queries

**Message-based queries with response objects**

| Implementation | Execution Time | Error | Memory Allocated | Performance vs Direct |
|:---------------|---------------:|------:|-----------------:|----------------------:|
| 🏆 **Direct Method Call** | **32.12 ns** | ±0.50 ns | **192 B** | **Baseline** |
| ⚡ **Foundatio Mediator** | **46.36 ns** | ±0.94 ns | **264 B** | **1.44x slower** |
| 🔶 MediatR | 81.40 ns | ±1.32 ns | 384 B | 2.53x slower |
| 🔴 MassTransit | 6,354.47 ns | ±125.37 ns | 12,784 B | 197.8x slower |

### Event Notifications

**Publish/subscribe pattern with multiple handlers**

| Implementation | Execution Time | Error | Memory Allocated | Performance vs Direct |
|:---------------|---------------:|------:|-----------------:|----------------------:|
| 🏆 **Direct Method Call** | **8.12 ns** | ±0.18 ns | **0 B** | **Baseline** |
| ⚡ **Foundatio Mediator** | **121.57 ns** | ±0.80 ns | **672 B** | **15.0x slower** |
| 🔶 MediatR | 59.29 ns | ±1.13 ns | 288 B | 7.30x slower |
| 🔴 MassTransit | 1,697.53 ns | ±13.97 ns | 4,448 B | 209.0x slower |

### Dependency Injection Overhead

**Handler methods requiring injected dependencies**

| Implementation | Execution Time | Error | Memory Allocated | Performance vs Direct |
|:---------------|---------------:|------:|-----------------:|----------------------:|
| 🏆 **Direct Method Call** | **39.24 ns** | ±0.81 ns | **264 B** | **Baseline** |
| ⚡ **Foundatio Mediator** | **53.30 ns** | ±1.05 ns | **336 B** | **1.36x slower** |
| 🔶 MediatR | 79.97 ns | ±0.54 ns | 456 B | 2.04x slower |
| 🔴 MassTransit | 5,397.69 ns | ±61.05 ns | 12,857 B | 137.6x slower |

## Key Performance Insights

### 🏆 **Performance Advantages**

- **🚀 Near-Optimal Performance**: Only slight overhead vs direct method calls
- **⚡ vs MediatR**: **3.06x faster** for commands, **1.76x faster** for queries
- **🔥 vs MassTransit**: **88x faster** for commands, **137x faster** for queries
- **💾 Zero Allocation Commands**: Fire-and-forget operations have no GC pressure
- **🎯 Minimal DI Overhead**: Only 36% performance cost for dependency injection
- **📡 Efficient Publishing**: Event publishing scales well with multiple handlers

### 📈 **Why Foundatio Mediator is Faster**

1. **C# Interceptors**: Compile-time method replacement eliminates runtime reflection
2. **Source Generation**: No dynamic handler discovery or registration overhead
3. **Direct Static Calls**: Generated code calls handlers directly with minimal indirection
4. **Smart DI Integration**: Dependencies resolved only when needed, not preemptively

## Enabling/Disabling Interceptors

### Default Behavior

Interceptors are **enabled by default** and provide the best performance.

### Disabling Interceptors

You can disable interceptors via MSBuild property:

```xml
<PropertyGroup>
  <MediatorDisableInterceptors>true</MediatorDisableInterceptors>
</PropertyGroup>
```

When disabled, the mediator falls back to traditional DI-based handler resolution.

### Runtime vs Compile-time Behavior

```csharp
// With interceptors (compile-time)
var result = mediator.Invoke(command); // → Direct static call

// Without interceptors (runtime)
var result = mediator.Invoke(command); // → DI container lookup → reflection
```

## Interceptor Limitations

### Same Assembly Requirement

Interceptors only work for handlers in the **same assembly** as the mediator call:

```csharp
// ✅ Works with interceptors - same assembly
public class OrderController : ControllerBase
{
    public async Task<IActionResult> CreateOrder(CreateOrderCommand command)
    {
        // This gets intercepted if OrderHandler is in same assembly
        var result = await _mediator.Invoke(command);
        return Ok(result);
    }
}

public class OrderHandler // Same assembly
{
    public static Result<Order> Handle(CreateOrderCommand command) => /* ... */;
}
```

```csharp
// ❌ Falls back to DI - different assembly
// If OrderHandler is in different assembly, uses DI container
var result = await _mediator.Invoke(command);
```

### Cross-Assembly Handler Resolution

For cross-assembly scenarios, the mediator automatically falls back to DI-based resolution:

```csharp
// Assembly A: Web.dll
public class OrderController : ControllerBase
{
    public async Task<IActionResult> CreateOrder(CreateOrderCommand command)
    {
        // Falls back to DI if handler is in different assembly
        var result = await _mediator.Invoke(command);
        return Ok(result);
    }
}

// Assembly B: Handlers.dll
public class OrderHandler
{
    public static Result<Order> Handle(CreateOrderCommand command) => /* ... */;
}
```

The DI registration generator ensures handlers are available:

```csharp
// Generated registration
services.AddKeyedTransient<HandlerRegistration>("CreateOrderCommand",
    new HandlerRegistration(typeof(CreateOrderCommand), typeof(OrderHandler), "Handle"));
```

## Optimizing for Maximum Performance

### 1. Keep Handlers in Same Assembly

```csharp
// Optimal structure - everything in same assembly
MyApp.dll:
├── Controllers/
│   └── OrderController.cs
├── Handlers/
│   └── OrderHandler.cs
└── Messages/
    └── CreateOrderCommand.cs
```

### 2. Use Static Handlers

```csharp
// Faster - static method (no instance creation)
public class OrderHandler
{
    public static Result<Order> Handle(CreateOrderCommand command)
    {
        return CreateOrder(command);
    }
}

// Slower - instance method (requires DI instantiation)
public class OrderHandler
{
    public Result<Order> Handle(CreateOrderCommand command)
    {
        return CreateOrder(command);
    }
}
```

### 3. Minimize Dependencies in Static Handlers

```csharp
// Optimal - minimal dependencies via parameters
public static Result<Order> Handle(
    CreateOrderCommand command,
    IOrderRepository repository)  // Injected only when needed
{
    return repository.Create(command);
}

// Suboptimal - heavy constructor dependencies
public class OrderHandler
{
    private readonly IOrderRepository _repo;
    private readonly IEmailService _email;
    private readonly ILogger _logger;
    private readonly IEventBus _events;
    // ... many dependencies

    public OrderHandler(/* many constructor parameters */) { }
}
```

### 4. Batch Operations

```csharp
// Efficient - batch multiple operations
public static Result<Order[]> Handle(CreateOrderBatchCommand command)
{
    return command.Orders.Select(CreateOrder).ToArray();
}

// Inefficient - individual calls in loop
foreach (var orderCommand in commands)
{
    await mediator.Invoke(orderCommand); // Multiple interceptor calls
}
```

## Performance Monitoring

### Built-in Diagnostics

The mediator includes built-in activity source for monitoring:

```csharp
using var activity = MediatorActivitySource.StartActivity("Invoke");
activity?.SetTag("message.type", typeof(TMessage).Name);
activity?.SetTag("handler.type", handlerType.Name);
```

### Integration with Application Insights

```csharp
builder.Services.AddApplicationInsightsTelemetry();

// Mediator calls will automatically appear in telemetry
public class OrderController : ControllerBase
{
    public async Task<IActionResult> CreateOrder(CreateOrderCommand command)
    {
        // This call will be tracked in Application Insights
        var result = await _mediator.Invoke(command);
        return Ok(result);
    }
}
```

### Custom Performance Monitoring

```csharp
public class PerformanceMiddleware
{
    public static (Stopwatch Timer, string Operation) Before(object message)
    {
        var timer = Stopwatch.StartNew();
        var operation = $"Handle{message.GetType().Name}";
        return (timer, operation);
    }

    public static void After(
        object message,
        object? response,
        Stopwatch timer,
        string operation,
        ILogger<PerformanceMiddleware> logger)
    {
        timer.Stop();

        if (timer.ElapsedMilliseconds > 100) // Log slow operations
        {
            logger.LogWarning("Slow operation {Operation} took {ElapsedMs}ms",
                operation, timer.ElapsedMilliseconds);
        }

        logger.LogDebug("Operation {Operation} completed in {ElapsedMs}ms",
            operation, timer.ElapsedMilliseconds);
    }
}
```

## Source Generator Performance

### Compilation Impact

The source generators add minimal compilation overhead:

- **Cold build**: +50-200ms (depending on project size)
- **Incremental build**: +5-20ms
- **Generated code size**: ~1-5KB per handler

### Generated Code Efficiency

The generated interceptors are highly optimized:

```csharp
// Minimal generated code - no abstractions
[InterceptsLocation("Program.cs", 42, 15)]
public static async Task<Result<Order>> Intercept_CreateOrder(
    this IMediator mediator,
    CreateOrderCommand command,
    CancellationToken cancellationToken = default)
{
    // Direct call with minimal overhead
    return await OrderHandler.Handle(command, cancellationToken);
}
```

## Memory Allocation Patterns

### Zero-Allocation Scenarios

```csharp
// Zero allocations for simple handlers
public static int Handle(GetCountQuery query)
{
    return _cache.GetCount(); // Returns value type directly
}

// Zero allocations for value type messages
public readonly record struct GetCountQuery;
```

### Minimal Allocation Scenarios

```csharp
// Minimal allocations - only for necessary objects
public static Result<Order> Handle(CreateOrderCommand command)
{
    return new Order { Email = command.Email }; // Only allocates Order
}
```

### Avoiding Allocations

```csharp
// ❌ Avoid - creates unnecessary collections
public static IEnumerable<Order> Handle(GetOrdersQuery query)
{
    return orders.Where(o => o.Status == query.Status).ToList(); // Extra allocation
}

// ✅ Better - streaming results
public static async IAsyncEnumerable<Order> Handle(GetOrdersQuery query)
{
    await foreach (var order in GetOrdersAsync())
    {
        if (order.Status == query.Status)
            yield return order; // No intermediate collections
    }
}
```

## Profiling and Benchmarking

### BenchmarkDotNet Integration

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class MediatorBenchmarks
{
    private IMediator _mediator;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        _mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Benchmark]
    public async Task<Result<Order>> InvokeCreateOrder()
    {
        return await _mediator.Invoke(new CreateOrderCommand("test@example.com"));
    }
}
```

### Performance Testing

```csharp
[Fact]
public async Task Should_Handle_High_Throughput()
{
    var tasks = new List<Task>();

    // Simulate 10,000 concurrent requests
    for (int i = 0; i < 10_000; i++)
    {
        tasks.Add(_mediator.Invoke(new CreateOrderCommand($"user{i}@example.com")));
    }

    var stopwatch = Stopwatch.StartNew();
    await Task.WhenAll(tasks);
    stopwatch.Stop();

    // Should complete in reasonable time with minimal memory usage
    Assert.True(stopwatch.ElapsedMilliseconds < 5000);
}
```

## Best Practices for Performance

### 1. Design for Interceptors

```csharp
// ✅ Interceptor-friendly - same assembly, static method
public class OrderHandler
{
    public static Result<Order> Handle(CreateOrderCommand command)
    {
        return CreateOrder(command);
    }
}
```

### 2. Use Async Appropriately

```csharp
// ✅ Async when doing I/O
public static async Task<Result<Order>> Handle(CreateOrderCommand command, IRepository repo)
{
    return await repo.CreateAsync(command.ToOrder());
}

// ✅ Sync when no I/O
public static Result<Order> Handle(CreateOrderCommand command)
{
    return new Order { Email = command.Email };
}
```

### 3. Optimize Message Design

```csharp
// ✅ Lightweight messages
public readonly record struct GetCountQuery;
public record CreateOrderCommand(string Email, decimal Amount);

// ❌ Heavy messages
public class CreateOrderCommand
{
    public Customer Customer { get; set; }
    public Product[] Products { get; set; }
    public ShippingInfo Shipping { get; set; }
    // ... large object graph
}
```

### 4. Leverage Streaming for Large Data

```csharp
// ✅ Stream large result sets
public static async IAsyncEnumerable<Order> Handle(GetAllOrdersQuery query)
{
    await foreach (var order in repository.GetOrdersStreamAsync())
        yield return order;
}

// ❌ Load everything into memory
public static Task<Order[]> Handle(GetAllOrdersQuery query)
{
    return repository.GetAllOrdersAsync(); // Could be huge!
}
```

Foundatio Mediator's interceptor-based approach provides exceptional performance while maintaining clean, maintainable code. By understanding how interceptors work and following performance best practices, you can build highly efficient applications that scale to handle millions of requests.
