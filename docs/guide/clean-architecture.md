---
title: "Clean Architecture"
nav:
    section: "Core Concepts"
    sectionOrder: 20
    order: 80
---

# Clean Architecture with Foundatio Mediator

Foundatio Mediator is designed to be a natural fit for Clean Architecture
applications. Its convention-based approach eliminates boilerplate while
enforcing clear boundaries between layers, making it easier to build
maintainable, testable, and loosely-coupled systems.

## Why Clean Architecture?

### Preventing the "Big Ball of Mud"

Without architectural discipline, applications naturally devolve into a **"Big
Ball of Mud"**—a haphazardly structured system where everything depends on
everything else. This happens when:

- Endpoints directly instantiate repositories and services
- Business logic leaks into presentation and database layers
- Infrastructure concerns (emails, caching, logging) are scattered throughout
- Circular dependencies create impossible-to-test code
- Making a change in one place breaks unrelated features

**Clean Architecture prevents this chaos through enforced loose coupling.** By
organizing code into layers with strict dependency rules, you create natural
boundaries that prevent tight coupling from forming.

### Layer Structure and Dependency Rules

Clean Architecture organizes code into concentric layers where dependencies
point inward—outer layers depend on inner layers, never the reverse:

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

**Key principle:** The Domain layer never depends on Infrastructure.
Infrastructure implements interfaces defined by the Domain/Application layers
(Dependency Inversion Principle).

### How Loose Coupling Emerges

This structure enforces loose coupling at every level:

- **Endpoints** don't know about repositories, email services, or databases—they
  just send messages
- **Handlers** depend only on abstractions (interfaces), not concrete
  implementations
- **Domain entities** have no framework dependencies—they're just plain C#
  objects
- **Infrastructure** can be swapped without touching business logic

The result: each piece of the system can evolve independently without creating a
ripple effect of breaking changes.

### Benefits of This Approach

- **Testability** - Business logic can be tested without frameworks or databases
- **Maintainability** - Changes in one layer don't cascade to others
- **Flexibility** - Infrastructure can be swapped without changing business
  rules
- **Focus** - Each layer has a single responsibility
- **Scalability** - Teams can work on different layers without conflicts

## The Mediator Pattern in Clean Architecture

The mediator pattern is the perfect complement to Clean Architecture because it
**decouples the caller from the handler**. Instead of your presentation layer
knowing about services, repositories, and business logic, it simply sends
messages.

### Automatic Endpoint Generation

With Foundatio Mediator's source generator, you can **eliminate the presentation
layer boilerplate entirely**. Your codebase stays completely message-oriented:
handlers continue to receive messages and return results, while the generated
endpoints remove the glue code between those handlers and the HTTP API.

HTTP endpoints are automatically generated from your handlers:

```csharp
public record CreateOrder(string CustomerId, decimal Amount);
public record GetOrder(string OrderId);

public class OrderHandler
{
    public Task<Result<Order>> HandleAsync(CreateOrder command, IOrderRepository orders, CancellationToken ct)
        => orders.CreateAsync(command, ct);

    public Task<Result<Order>> HandleAsync(GetOrder query, IOrderRepository orders, CancellationToken ct)
        => orders.GetByIdAsync(query.OrderId, ct);
}
```

The source generator automatically creates:

- `POST /api/orders` → calls `CreateOrder` handler
- `GET /api/orders/{orderId}` → calls `GetOrder` handler

No endpoint attributes are required for this example. The generator discovers
`OrderHandler` by convention, derives the `orders` endpoint group from the class
name, and uses the message names and properties to infer routes. Add
`[HandlerEndpointGroup]` only when you want to override the group name or route
prefix, attach group-level filters, or opt into endpoint generation when
discovery is set to explicit.

**No controller classes. No endpoint registrations. No boilerplate.** Just map
them in your startup:

```csharp
app.MapMediatorEndpoints();
```

The generator infers HTTP methods from message names (`Create*` → POST, `Get*` →
GET), generates routes from ID properties, maps `Result<T>` to HTTP status
codes, and pulls OpenAPI metadata from XML doc comments.

### Manual Endpoints (When Needed)

If you prefer explicit control or need custom endpoint behavior, you can still
write manual endpoints:

```csharp
app.MapPost("/orders", async (CreateOrder command, IMediator mediator) =>
{
    var result = await mediator.InvokeAsync<Result<Order>>(command);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.Message);
});
```

Either way, your presentation layer stays thin and focused on HTTP concerns
while business logic lives entirely in handlers.

## How Foundatio Mediator Enables Clean Architecture

### 1. Low-Ceremony Handler Definition

Unlike traditional mediator implementations that require interface inheritance
and rigid method signatures, Foundatio Mediator uses conventions:

```csharp
// Traditional mediator libraries - lots of ceremony
public class CreateOrderHandler : IRequestHandler<CreateOrder, Result<Order>>
{
    public Task<Result<Order>> Handle(CreateOrder request, CancellationToken ct)
        => CreateOrder(request, ct);
}

// Foundatio Mediator - just follow naming conventions
public class OrderHandler
{
    public Task<Result<Order>> HandleAsync(CreateOrder command, IOrderRepository orders, CancellationToken ct)
        => orders.CreateAsync(command, ct);
}
```

**Benefits:**

- No interface inheritance required
- Method injection means less boilerplate
- Multiple handlers per class for related operations
- Sync or async—you choose based on your needs

### 2. Natural Command/Query Separation (CQRS)

Clean Architecture naturally leads to CQRS because queries and commands have
different characteristics. Foundatio Mediator makes this separation effortless:

```csharp
public record CreateOrder(string CustomerId, decimal Amount);
public record GetOrder(string OrderId);

public class OrderHandler
{
    public Task<Result<Order>> HandleAsync(CreateOrder command, IOrderRepository orders, CancellationToken ct)
        => orders.CreateAsync(command, ct);

    public Task<Result<Order>> HandleAsync(GetOrder query, IOrderRepository orders, CancellationToken ct)
        => orders.GetByIdAsync(query.OrderId, ct);
}
```

### 3. Domain Events for Loose Coupling

When a business operation completes, other parts of the system often need to
react—send emails, update analytics, log audits. Traditional approaches create
tight coupling:

```csharp
// Tight coupling - handler knows about all side effects
public async Task<Order> HandleAsync(CreateOrder command)
{
    var order = await _repository.CreateAsync(command);

    await _emailService.SendOrderConfirmationAsync(order);
    await _auditService.LogOrderCreatedAsync(order);

    return order;
}
```

With Foundatio Mediator's cascading messages, handlers publish events and don't
care who handles them:

```csharp
// Loose coupling - handler just publishes an event
public async Task<(Result<Order>, OrderCreated)> HandleAsync(CreateOrder command, IOrderRepository orders, CancellationToken ct)
{
    var order = await orders.CreateAsync(command, ct);

    return (order, new OrderCreated(order.Id, order.CustomerId, DateTime.UtcNow));
}

public class AuditHandler
{
    public Task HandleAsync(OrderCreated evt, IAuditService audit, CancellationToken ct)
        => audit.LogAsync("OrderCreated", evt.OrderId, ct);
}
```

**Benefits:**

- Adding new reactions requires zero changes to the publishing handler
- Event handlers can live in different modules/assemblies
- Easy to test each handler in isolation
- Clear audit trail of system behavior

### 4. Modular Monolith Support

Clean Architecture shines in modular monoliths where bounded contexts are
separated into modules. Foundatio Mediator enables cross-module communication
without coupling modules to each other's repositories or persistence details:

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
// Reports.Module depends on message contracts, not repositories or persistence.
// It queries other modules through the mediator.
public class ReportHandler
{
    public async Task<Result<DashboardReport>> HandleAsync(
        GetDashboardReport report,
        IMediator mediator,
        CancellationToken ct)
    {
        var orders = await mediator.InvokeAsync<Result<List<Order>>>(new GetOrders(), ct);
        var products = await mediator.InvokeAsync<Result<List<Product>>>(new GetProducts(), ct);

        // Build the report without depending on either module's data layer.
        return BuildDashboard(orders.Value, products.Value);
    }
}
```

### 5. Cross-Cutting Concerns via Middleware

Clean Architecture requires cross-cutting concerns (logging, validation,
caching) to be handled consistently without polluting business logic. Foundatio
Mediator's middleware pipeline makes this natural:

```csharp
// Validation middleware - runs before every handler
public class ValidationMiddleware
{
    public HandlerResult Before(object message)
    {
        if (!IsValid(message, out var validationErrors))
            return HandlerResult.ShortCircuit(Result.Invalid(validationErrors));

        return HandlerResult.Continue();
    }
}

// Logging middleware - tracks all handler execution
public class ObservabilityMiddleware
{
    public Stopwatch Before(object message, HandlerExecutionInfo info, ILogger logger)
        => Stopwatch.StartNew();

    public void Finally(object message, Stopwatch sw, Exception? ex, ILogger logger)
        => logger.LogInformation("Handled {Message} in {Ms}ms", message.GetType().Name, sw.ElapsedMilliseconds);
}
```

Middleware is automatically applied to all handlers—no manual registration or
decorator patterns needed.

### 6. Zero-Boilerplate HTTP Endpoints

Traditional Clean Architecture implementations still require significant
presentation layer code—controllers, endpoint registrations, parameter binding,
and response mapping. Foundatio Mediator's source generator eliminates this
entirely:

```csharp
// Traditional approach - every operation needs HTTP glue code
public class OrdersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var result = await _mediator.InvokeAsync<Result<Order>>(MapToCommand(request));
        return MapToHttp(result);
    }
}

// Foundatio Mediator - the handler is enough
public class OrderHandler
{
    public Task<Result<Order>> HandleAsync(CreateOrder command, IOrderRepository orders, CancellationToken ct)
        => orders.CreateAsync(command, ct);
}
```

**Benefits:**

- **No controllers or endpoint classes** - handlers define the API surface
- **Automatic HTTP method inference** - `Create*` → POST, `Get*` → GET,
  `Update*` → PUT, `Delete*` → DELETE
- **Automatic route generation** - ID properties become route parameters
- **Result-to-HTTP mapping** - `Result.NotFound()` → 404, `Result.Invalid()` →
  400, etc.
- **OpenAPI generation** - XML doc comments become endpoint summaries
- **Authentication built-in** - Configure auth at group or endpoint level

See [Automatic Endpoint Generation](./endpoints) for full documentation.

## Project Structure Example

Here's a recommended structure for a Clean Architecture application using
Foundatio Mediator:

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
    └── Program.cs                  # Endpoints auto-generated from handlers
```

## Real-World Example

See the
[CleanArchitectureSample](https://github.com/FoundatioFx/Foundatio.Mediator/tree/main/samples/CleanArchitectureSample)
in the repository for a complete working example demonstrating:

- Multiple bounded contexts (Orders, Products, Reports)
- Cross-module communication via mediator
- Domain events with cascading messages
- Shared middleware for validation and observability
- Repository pattern with in-memory implementations

## Key Benefits Summary

| Traditional Approach                                        | With Foundatio Mediator                             |
| ----------------------------------------------------------- | --------------------------------------------------- |
| Endpoints know about services, repositories, business logic | Endpoints only know about messages and mediator     |
| Tight coupling between modules                              | Loose coupling via messages and events              |
| Interface boilerplate for every handler                     | Convention-based discovery, zero interfaces         |
| Manual event publishing and subscription                    | Automatic cascading with tuple returns              |
| Cross-cutting concerns scattered or complex decorators      | Simple middleware with Before/After/Finally/Execute |
| One handler per class limitation                            | Multiple handlers per class, grouped naturally      |
| Controllers/endpoints for every operation                   | Auto-generated endpoints from handlers              |
| Manual HTTP status code mapping                             | Result types map to HTTP status automatically       |

## Next Steps

- [Getting Started](./getting-started) - Set up Foundatio Mediator
- [Automatic Endpoint Generation](./endpoints) - Zero-boilerplate HTTP APIs
- [Cascading Messages](./cascading-messages) - Domain events and event-driven
  architecture
- [Middleware](./middleware) - Cross-cutting concerns
- [CleanArchitectureSample](https://github.com/FoundatioFx/Foundatio.Mediator/tree/main/samples/CleanArchitectureSample) -
  Complete working example
