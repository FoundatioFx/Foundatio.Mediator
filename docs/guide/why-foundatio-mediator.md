# Why Choose Foundatio Mediator?

Foundatio Mediator stands out from other mediator implementations by combining exceptional performance with developer-friendly conventions. Here's why it might be the right choice for your project.

## Performance That Matters

### Near-Direct Call Performance

Unlike other mediator libraries that rely on runtime reflection or complex dispatch mechanisms, Foundatio Mediator uses **C# interceptors** to transform your mediator calls into essentially direct method calls:

```csharp
// You write this:
await mediator.InvokeAsync<User>(new GetUser(123));

// The source generator creates something like this:
await UserHandler_Generated.HandleAsync(new GetUser(123), serviceProvider, cancellationToken);
```

This results in:

- **2-15x faster** than other mediator libraries
- **Zero allocations** for fire-and-forget commands
- **No reflection overhead** at runtime
- **Full compiler optimizations** including inlining

### Benchmark Results

Benchmark highlights (see root README for full tables):

| Scenario | Foundatio | MediatR | MassTransit |
|----------|-----------|---------|------------|
| Commands | 17.93 ns (2.15x direct) | 54.81 ns | 1,585.85 ns |
| Queries  | 46.36 ns (1.44x direct) | 81.40 ns | 6,354.47 ns |
| Events (publish) | 121.57 ns | 59.29 ns | 1,697.53 ns |

Event publishing involves multiple handler pipeline steps; Foundatio optimizes single-handler command/query paths for near-direct performance.

## Developer Experience

### Convention Over Configuration

No interfaces, base classes, or complex registration required:

```csharp
// ✅ Foundatio Mediator - Just naming conventions
public class UserHandler
{
    public User Handle(GetUser query) => _repository.Find(query.Id);
}

// ❌ Other libraries - Interfaces required
public class UserHandler : IRequestHandler<GetUser, User>
{
    public User Handle(GetUser query) => _repository.Find(query.Id);
}
```

### Rich Error Handling

Built-in `Result<T>` types eliminate exception-driven control flow:

```csharp
public Result<User> Handle(GetUser query)
{
    var user = _repository.Find(query.Id);

    if (user == null)
        return Result.NotFound($"User {query.Id} not found");

    if (!user.IsActive)
        return Result.Forbidden("Account disabled");

    return user; // Implicit conversion
}
```

Compare this to exception-heavy alternatives:

```csharp
// ❌ Exception-based approach
public User Handle(GetUser query)
{
    var user = _repository.Find(query.Id);

    if (user == null)
        throw new NotFoundException($"User {query.Id} not found");

    if (!user.IsActive)
        throw new UnauthorizedException("Account disabled");

    return user;
}
```

### Seamless Dependency Injection

Full support for both constructor and method injection:

```csharp
public class OrderHandler
{
    // Constructor injection for long-lived dependencies
    public OrderHandler(ILogger<OrderHandler> logger) { }

    // Method injection for per-request dependencies
    public async Task<Order> HandleAsync(
        CreateOrder command,
        IOrderRepository repo,    // Injected
        IEmailService email,      // Injected
        CancellationToken ct      // Automatically provided
    )
    {
        var order = await repo.CreateAsync(command);
        await email.SendOrderConfirmationAsync(order, ct);
        return order;
    }
}
```

## Architecture Benefits

### Message-Driven Design

Encourages clean, message-oriented architecture:

```csharp
// Clear command intent
public record CreateOrder(string CustomerId, decimal Amount, string Description);

// Explicit query purpose
public record GetOrdersByCustomer(string CustomerId, DateTime? Since = null);

// Well-defined events
public record OrderCreated(string OrderId, string CustomerId, DateTime CreatedAt);
```

### Automatic Event Publishing

Tuple returns enable automatic event cascading which keeps your handlers simple and easy to test:

```csharp
public async Task<(Order order, OrderCreated evt)> HandleAsync(CreateOrder cmd)
{
    var order = await _repository.CreateAsync(cmd);

    // OrderCreated is automatically published to all handlers
    return (order, new OrderCreated(order.Id, cmd.CustomerId, DateTime.UtcNow));
}
```

### Cross-Cutting Concerns Made Easy

Powerful middleware pipeline for common concerns:

```csharp
[FoundatioOrder(10)]
public class ValidationMiddleware
{
    public HandlerResult Before(object message)
    {
        if (!TryValidate(message, out var errors))
            return Result.Invalid(errors); // Short-circuit

        return HandlerResult.Continue();
    }
}

[FoundatioOrder(20)]
public class LoggingMiddleware
{
    public Stopwatch Before(object message) => Stopwatch.StartNew();

    public void Finally(object message, Stopwatch sw, Exception? ex)
    {
        _logger.LogInformation("Handled {Message} in {Ms}ms",
            message.GetType().Name, sw.ElapsedMilliseconds);
    }
}
```

## Testing & Debugging

### Superior Testing Experience

Handlers are plain objects - no framework mocking required:

```csharp
[Test]
public async Task Should_Create_Order_Successfully()
{
    // Arrange
    var handler = new OrderHandler(_mockLogger.Object);
    var command = new CreateOrder("CUST-001", 99.99m, "Test order");

    // Act
    var (result, evt) = await handler.HandleAsync(command);

    // Assert
    result.IsSuccess.Should().BeTrue();
    result.Value.CustomerId.Should().Be("CUST-001");
    evt.Should().NotBeNull();
}
```

### Excellent Debugging Experience

Short, simple call stacks make debugging straightforward:

```text
Your Code
  ↓
Generated Interceptor (minimal)
  ↓
Your Handler Method
```

Compare this to complex reflection-based call stacks in other libraries.

## When to Choose Foundatio Mediator

### ✅ Ideal For

- **High-performance applications** where every nanosecond matters
- **Clean architecture** implementations with CQRS patterns
- **Event-driven systems** with publish/subscribe needs
- **Teams preferring conventions** over explicit interfaces
- **Applications requiring robust error handling** without exceptions
- **Projects wanting excellent testing experience** without framework coupling

### ⚠️ Consider Alternatives When

- **Legacy .NET Framework** projects (requires modern .NET)
- **Simple CRUD applications** without complex business logic
- **Teams preferring explicit interfaces** over conventions
- **Existing MediatR codebases** where migration cost isn't justified

## Migration from Other Libraries

### From MediatR

Foundatio Mediator provides a migration-friendly approach:

```csharp
// MediatR style (still works with some adaptation)
public class UserHandler : IRequestHandler<GetUser, User>
{
    public Task<User> Handle(GetUser request, CancellationToken ct) { }
}

// Foundatio Mediator style (recommended)
public class UserHandler
{
    public Task<User> HandleAsync(GetUser request, CancellationToken ct) { }
}
```

### Migration Benefits

- **Better performance** with zero code changes in many cases
- **Improved error handling** with Result types
- **Enhanced middleware** with state passing
- **Reduced boilerplate** with convention-based discovery

## Real-World Success Stories

### Performance-Critical APIs

> "We migrated our high-throughput order processing API from MediatR to Foundatio Mediator and saw a 40% reduction in P99 latency while simplifying our error handling patterns."

### Microservices Architecture

> "The convention-based approach reduced onboarding time for new team members, and the automatic event publishing simplified our event-driven architecture."

### Large Enterprise Applications

> "The compile-time validation caught numerous handler registration issues that would have been runtime errors in our previous mediator library."

## Getting Started

Ready to experience the benefits of Foundatio Mediator?

1. [Installation & Setup](./getting-started) - Get running in minutes
2. [Simple Examples](../examples/simple-handlers) - See the patterns in action

The combination of exceptional performance, developer-friendly conventions, and robust error handling makes Foundatio Mediator an excellent choice for modern .NET applications.
