# Clean Architecture with Foundatio Mediator

Foundatio Mediator is designed to be a natural fit for Clean Architecture applications. Its convention-based approach eliminates boilerplate while enforcing clear boundaries between layers, making it easier to build maintainable, testable, and loosely-coupled systems.

## Why Clean Architecture?

### Preventing the "Big Ball of Mud"

Without architectural discipline, applications naturally devolve into a **"Big Ball of Mud"**—a haphazardly structured system where everything depends on everything else. This happens when:

- Endpoints directly instantiate repositories and services
- Business logic leaks into presentation and database layers
- Infrastructure concerns (emails, caching, logging) are scattered throughout
- Circular dependencies create impossible-to-test code
- Making a change in one place breaks unrelated features

**Clean Architecture prevents this chaos through enforced loose coupling.** By organizing code into layers with strict dependency rules, you create natural boundaries that prevent tight coupling from forming.

### Layer Structure and Dependency Rules

Clean Architecture organizes code into concentric layers where dependencies point inward—outer layers depend on inner layers, never the reverse:

```text
┌──────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│  (Endpoints, APIs, UI - knows about everything below)        │
├──────────────────────────────────────────────────────────────┤
│                   Application Layer                          │
│  (Handlers, Use Cases - knows about Domain only)             │
├──────────────────────────────────────────────────────────────┤
│                 Infrastructure Layer                         │
│  (Repositories, External Services - implements interfaces)   │
├──────────────────────────────────────────────────────────────┤
│                     Domain Layer                             │
│  (Entities, Value Objects - no external dependencies)        │
└──────────────────────────────────────────────────────────────┘
```

**Key principle:** The Domain layer never depends on Infrastructure. Infrastructure implements interfaces defined by the Domain/Application layers (Dependency Inversion Principle).

### How Loose Coupling Emerges

This structure enforces loose coupling at every level:

- **Endpoints** don't know about repositories, email services, or databases—they just send messages
- **Handlers** depend only on abstractions (interfaces), not concrete implementations
- **Domain entities** have no framework dependencies—they're just plain C# objects
- **Infrastructure** can be swapped without touching business logic

The result: each piece of the system can evolve independently without creating a ripple effect of breaking changes.

### Benefits of This Approach

- **Testability** - Business logic can be tested without frameworks or databases
- **Maintainability** - Changes in one layer don't cascade to others
- **Flexibility** - Infrastructure can be swapped without changing business rules
- **Focus** - Each layer has a single responsibility
- **Scalability** - Teams can work on different layers without conflicts

## The Mediator Pattern in Clean Architecture

The mediator pattern is the perfect complement to Clean Architecture because it **decouples the caller from the handler**. Instead of your presentation layer knowing about services, repositories, and business logic, it simply sends messages:

```csharp
// MVC Controller
[HttpPost]
public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
{
    var result = await _mediator.InvokeAsync<Result<Order>>(
        new CreateOrder(request.CustomerId, request.Amount));
    return result.ToActionResult();
}

// Minimal API
app.MapPost("/orders", async (CreateOrderRequest request, IMediator mediator) =>
{
    var result = await mediator.InvokeAsync<Result<Order>>(
        new CreateOrder(request.CustomerId, request.Amount));
    return result.ToActionResult();
});
```

Either way, your presentation layer stays thin and focused on HTTP concerns while business logic lives entirely in handlers.

## How Foundatio Mediator Enables Clean Architecture

### 1. Low-Ceremony Handler Definition

Unlike traditional mediator implementations that require interface inheritance and rigid method signatures, Foundatio Mediator uses conventions:

```csharp
// Traditional mediator libraries - lots of ceremony
public class CreateOrderHandler : IRequestHandler<CreateOrder, Result<Order>>
{
    private readonly IOrderRepository _repository;

    public CreateOrderHandler(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Order>> Handle(CreateOrder request, CancellationToken ct)
    {
        // Business logic
    }
}

// Foundatio Mediator - just follow naming conventions
public class OrderHandler
{
    public async Task<Result<Order>> HandleAsync(
        CreateOrder command,
        IOrderRepository repository,  // Method injection - no constructor needed
        CancellationToken ct)
    {
        // Business logic
    }
}
```

**Benefits:**

- No interface inheritance required
- Method injection means less boilerplate
- Multiple handlers per class for related operations
- Sync or async—you choose based on your needs

### 2. Natural Command/Query Separation (CQRS)

Clean Architecture naturally leads to CQRS because queries and commands have different characteristics. Foundatio Mediator makes this separation effortless:

```csharp
// Commands - change state, return results
public record CreateOrder(string CustomerId, decimal Amount);
public record UpdateOrderStatus(string OrderId, OrderStatus Status);
public record CancelOrder(string OrderId, string Reason);

// Queries - read state, never modify
public record GetOrder(string OrderId);
public record GetOrdersByCustomer(string CustomerId, DateTime? Since = null);
public record GetDashboardReport();

// Handler can group related operations naturally
public class OrderHandler
{
    // Commands
    public async Task<Result<Order>> HandleAsync(CreateOrder cmd, IOrderRepository repo, CancellationToken ct)
        => await repo.CreateAsync(cmd, ct);

    public async Task<Result<Order>> HandleAsync(UpdateOrderStatus cmd, IOrderRepository repo, CancellationToken ct)
        => await repo.UpdateStatusAsync(cmd.OrderId, cmd.Status, ct);

    // Queries
    public async Task<Result<Order>> HandleAsync(GetOrder query, IOrderRepository repo, CancellationToken ct)
        => await repo.GetByIdAsync(query.OrderId, ct);

    public async Task<Result<IReadOnlyList<Order>>> HandleAsync(GetOrdersByCustomer query, IOrderRepository repo, CancellationToken ct)
        => await repo.GetByCustomerAsync(query.CustomerId, query.Since, ct);
}
```

### 3. Domain Events for Loose Coupling

When a business operation completes, other parts of the system often need to react—send emails, update analytics, log audits. Traditional approaches create tight coupling:

```csharp
// Tight coupling - handler knows about all side effects
public async Task<Order> HandleAsync(CreateOrder cmd)
{
    var order = await _repository.CreateAsync(cmd);

    await _emailService.SendOrderConfirmationAsync(order);  // Coupling
    await _analyticsService.TrackOrderAsync(order);         // Coupling
    await _auditService.LogOrderCreatedAsync(order);        // Coupling

    return order;
}
```

With Foundatio Mediator's cascading messages, handlers publish events and don't care who handles them:

```csharp
// Loose coupling - handler just publishes an event
public async Task<(Result<Order>, OrderCreated)> HandleAsync(
    CreateOrder cmd,
    IOrderRepository repository,
    CancellationToken ct)
{
    var order = await repository.CreateAsync(cmd, ct);

    // Return the result AND an event - mediator publishes it automatically
    return (order, new OrderCreated(order.Id, order.CustomerId, DateTime.UtcNow));
}

// Event handlers are completely decoupled - add/remove without touching OrderHandler
public class EmailHandler
{
    public async Task HandleAsync(OrderCreated evt, IEmailService email, CancellationToken ct)
        => await email.SendOrderConfirmationAsync(evt.OrderId, ct);
}

public class AnalyticsHandler
{
    public async Task HandleAsync(OrderCreated evt, IAnalyticsService analytics, CancellationToken ct)
        => await analytics.TrackOrderAsync(evt.OrderId, evt.CustomerId, ct);
}

public class AuditHandler
{
    public async Task HandleAsync(OrderCreated evt, IAuditService audit, CancellationToken ct)
        => await audit.LogAsync("OrderCreated", evt.OrderId, ct);
}
```

**Benefits:**

- Adding new reactions requires zero changes to the publishing handler
- Event handlers can live in different modules/assemblies
- Easy to test each handler in isolation
- Clear audit trail of system behavior

### 4. Modular Monolith Support

Clean Architecture shines in modular monoliths where bounded contexts are separated into modules. Foundatio Mediator enables cross-module communication without creating dependencies:

```text
┌──────────────────────────────────────────────────────────────┐
│                     Common.Module                            │
│  Events, Middleware, Shared Services                         │
├──────────────────────┬───────────────────────────────────────┤
│  Orders.Module       │         Products.Module               │
│  OrderHandler        │         ProductHandler                │
│  Order Domain        │         Product Domain                │
├──────────────────────┴───────────────────────────────────────┤
│                    Reports.Module                            │
│  Queries data from Orders and Products via mediator          │
└──────────────────────────────────────────────────────────────┘
```

```csharp
// Reports.Module doesn't reference Orders or Products directly
// It queries through the mediator
public class ReportHandler
{
    public async Task<DashboardReport> HandleAsync(
        GetDashboardReport query,
        IMediator mediator,
        CancellationToken ct)
    {
        // Fetch from other modules via mediator - no direct dependencies
        var ordersTask = mediator.InvokeAsync<IReadOnlyList<Order>>(new GetOrders(), ct);
        var productsTask = mediator.InvokeAsync<IReadOnlyList<Product>>(new GetProducts(), ct);

        await Task.WhenAll(ordersTask.AsTask(), productsTask.AsTask());

        return new DashboardReport(
            TotalOrders: ordersTask.Result.Count,
            TotalProducts: productsTask.Result.Count,
            Revenue: ordersTask.Result.Sum(o => o.Amount)
        );
    }
}
```

### 5. Cross-Cutting Concerns via Middleware

Clean Architecture requires cross-cutting concerns (logging, validation, caching) to be handled consistently without polluting business logic. Foundatio Mediator's middleware pipeline makes this natural:

```csharp
// Validation middleware - runs before every handler
public class ValidationMiddleware
{
    public HandlerResult Before(object message)
    {
        if (!MiniValidator.TryValidate(message, out var errors))
            return Result.Invalid(errors);

        return HandlerResult.Continue();
    }
}

// Logging middleware - tracks all handler execution
public class ObservabilityMiddleware
{
    public Stopwatch Before(object message, HandlerExecutionInfo info, ILogger logger)
    {
        logger.LogInformation("Handling {Handler}", info.HandlerType.Name);
        return Stopwatch.StartNew();
    }

    public void After(object message, Stopwatch sw, HandlerExecutionInfo info, ILogger logger)
    {
        logger.LogInformation("Completed {Handler} in {Ms}ms",
            info.HandlerType.Name, sw.ElapsedMilliseconds);
    }

    public void Finally(object message, Stopwatch sw, Exception? ex, ILogger logger)
    {
        if (ex != null)
            logger.LogError(ex, "Handler failed after {Ms}ms", sw.ElapsedMilliseconds);
    }
}
```

Middleware is automatically applied to all handlers—no manual registration or decorator patterns needed.

## Project Structure Example

Here's a recommended structure for a Clean Architecture application using Foundatio Mediator:

```text
src/
├── Domain/                          # Pure domain - no dependencies
│   ├── Entities/
│   │   ├── Order.cs
│   │   └── Product.cs
│   └── ValueObjects/
│       └── Money.cs
│
├── Application/                     # Handlers and business logic
│   ├── Orders/
│   │   ├── Commands/
│   │   │   ├── CreateOrder.cs
│   │   │   └── CancelOrder.cs
│   │   ├── Queries/
│   │   │   ├── GetOrder.cs
│   │   │   └── GetOrdersByCustomer.cs
│   │   ├── Events/
│   │   │   └── OrderCreated.cs
│   │   └── OrderHandler.cs
│   ├── Products/
│   │   └── ...
│   └── Common/
│       ├── Middleware/
│       │   ├── ValidationMiddleware.cs
│       │   └── LoggingMiddleware.cs
│       └── Interfaces/
│           ├── IOrderRepository.cs
│           └── IProductRepository.cs
│
├── Infrastructure/                  # External concerns
│   ├── Persistence/
│   │   ├── OrderRepository.cs
│   │   └── ProductRepository.cs
│   └── Services/
│       └── EmailService.cs
│
└── Web/                            # Presentation layer
    ├── Endpoints/
    │   ├── OrderEndpoints.cs
    └── Program.cs
```

## Real-World Example

See the [CleanArchitectureSample](https://github.com/FoundatioFx/Foundatio.Mediator/tree/main/samples/CleanArchitectureSample) in the repository for a complete working example demonstrating:

- Multiple bounded contexts (Orders, Products, Reports)
- Cross-module communication via mediator
- Domain events with cascading messages
- Shared middleware for validation and observability
- Repository pattern with in-memory implementations

## Key Benefits Summary

| Traditional Approach | With Foundatio Mediator |
|---------------------|------------------------|
| Endpoints know about services, repositories, business logic | Endpoints only know about messages and mediator |
| Tight coupling between modules | Loose coupling via messages and events |
| Interface boilerplate for every handler | Convention-based discovery, zero interfaces |
| Manual event publishing and subscription | Automatic cascading with tuple returns |
| Cross-cutting concerns scattered or complex decorators | Simple middleware with Before/After/Finally/Execute |
| One handler per class limitation | Multiple handlers per class, grouped naturally |

## Next Steps

- [Getting Started](./getting-started) - Set up Foundatio Mediator
- [Cascading Messages](./cascading-messages) - Domain events and event-driven architecture
- [Middleware](./middleware) - Cross-cutting concerns
- [CleanArchitectureSample](https://github.com/FoundatioFx/Foundatio.Mediator/tree/main/samples/CleanArchitectureSample) - Complete working example
