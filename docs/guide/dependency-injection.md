# Dependency Injection

Foundatio Mediator seamlessly integrates with Microsoft.Extensions.DependencyInjection to provide powerful dependency injection capabilities for both handlers and middleware.

## Registration

Register the mediator and discover handlers in your DI container:

```csharp
using Foundatio.Mediator;

var builder = WebApplication.CreateBuilder(args);

// Register the mediator - this automatically discovers and registers handlers
builder.Services.AddMediator();

var app = builder.Build();
```

## Handler Lifetime Management

### Important: Handler Instances Are Cached When Not Registered

If you don't explicitly register a handler in DI, the mediator will create an instance via `ActivatorUtilities.CreateInstance` and cache that instance (effectively singleton behavior). Constructor dependencies resolved in that first construction are reused for all invocations. Register handlers explicitly to control lifetime or rely on method parameter injection for per-invocation dependencies.

```csharp
// WARNING: This handler is singleton - dependencies resolved once!
public class OrderHandler
{
    private readonly IOrderRepository _repository; // Resolved once, shared forever

    public OrderHandler(IOrderRepository repository)
    {
        _repository = repository; // This instance is reused for all requests!
    }

    public async Task<Result<Order>> Handle(CreateOrderCommand command)
    {
        // If IOrderRepository is scoped (like DbContext), this will cause issues!
        return await _repository.CreateAsync(command.ToOrder());
    }
}
```

### Automatic Handler Creation

Resolution order:

1. **Registered in DI**: DI creates according to configured lifetime.
2. **Not registered**: Created once and cached (no DI lifetime scoping).

### Explicit Handler Registration for Lifetime Control

To avoid singleton issues with scoped dependencies, register handlers explicitly:

```csharp
builder.Services.AddMediator();

// Register handlers with proper lifetimes to match their dependencies
builder.Services.AddScoped<OrderHandler>();    // Matches DbContext scope
builder.Services.AddTransient<EmailHandler>(); // New instance each time
builder.Services.AddSingleton<CacheHandler>(); // Truly singleton
```

### Automatic Handler Registration with MSBuild

You can automatically register all handlers in your project with a specific lifetime using the `MediatorDefaultMediatorLifetime` MSBuild property:

```xml
<PropertyGroup>
    <MediatorDefaultMediatorLifetime>Scoped</MediatorDefaultMediatorLifetime>
</PropertyGroup>
```

**Supported Values:**

- `Scoped` - Handlers registered as scoped services
- `Transient` - Handlers registered as transient services
- `Singleton` - Handlers registered as singleton services

**What this does:**

- Automatically registers all discovered handlers with the specified lifetime
- Eliminates the need for manual handler registration
- Ensures consistent lifetime management across your application
- Prevents singleton caching issues when using scoped dependencies

**Example usage:**

```xml
<!-- In your .csproj file -->
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <MediatorDefaultMediatorLifetime>Scoped</MediatorDefaultMediatorLifetime>
  </PropertyGroup>

  <PackageReference Include="Foundatio.Mediator" Version="1.0.0" />

</Project>
```

With this configuration, all your handlers will be automatically registered as scoped services:

```csharp
// No manual registration needed - this handler is automatically scoped
public class OrderHandler
{
    private readonly IOrderRepository _repository;

    public OrderHandler(IOrderRepository repository)
    {
        _repository = repository; // Safe: both are scoped
    }

    public async Task<Result<Order>> Handle(CreateOrderCommand command)
    {
        return await _repository.CreateAsync(command.ToOrder());
    }
}

// Just register the mediator - handlers are auto-registered
builder.Services.AddMediator();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
```

### Per-Handler Lifetime Override

Individual handlers can override the project-level default lifetime using the `[Handler]` attribute:

```csharp
// Uses project-level MediatorDefaultMediatorLifetime
public class DefaultHandler
{
    public Task HandleAsync(MyMessage msg) => Task.CompletedTask;
}

// Explicitly registered as Singleton (overrides project default)
[Handler(Lifetime = MediatorLifetime.Singleton)]
public class CacheHandler
{
    private readonly InMemoryCache _cache = new();

    public CachedData Handle(GetCachedData query) => _cache.Get(query.Key);
}

// Explicitly registered as Transient
[Handler(Lifetime = MediatorLifetime.Transient)]
public class StatelessHandler
{
    public void Handle(LogEvent evt) { /* ... */ }
}

// Explicitly registered as Scoped (even if project default is different)
[Handler(Lifetime = MediatorLifetime.Scoped)]
public class ScopedHandler
{
    private readonly DbContext _db;

    public ScopedHandler(DbContext db) => _db = db;

    public async Task<Order> HandleAsync(GetOrder query)
    {
        return await _db.Orders.FindAsync(query.Id);
    }
}
```

**Available `MediatorLifetime` values:**
- `MediatorLifetime.Default` - Use project-level `MediatorDefaultMediatorLifetime`
- `MediatorLifetime.Transient` - New instance per request
- `MediatorLifetime.Scoped` - Same instance within a scope
- `MediatorLifetime.Singleton` - Single instance for application lifetime

## Constructor Injection (Use with Caution)

**⚠️ Note:** Constructor injection without DI registration leads to a cached singleton-like instance.

```csharp
// PROBLEMATIC: Singleton handler with scoped dependency
public class OrderHandler
{
    private readonly IOrderRepository _repository; // DbContext-based repository

    public OrderHandler(IOrderRepository repository)
    {
        _repository = repository; // This DbContext instance lives forever!
    }

    public async Task<Result<Order>> Handle(CreateOrderCommand command)
    {
        // This will eventually fail - DbContext disposed but handler keeps reference
        return await _repository.CreateAsync(command.ToOrder());
    }
}
```

**Solution:** Register the handler with appropriate lifetime:

```csharp
// In Program.cs
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<OrderHandler>(); // Now handler matches repository lifetime

// Handler is now properly scoped
public class OrderHandler
{
    private readonly IOrderRepository _repository;

    public OrderHandler(IOrderRepository repository)
    {
        _repository = repository; // Safe: both handler and repo are scoped
    }

    public async Task<Result<Order>> Handle(CreateOrderCommand command)
    {
        return await _repository.CreateAsync(command.ToOrder());
    }
}
```

## Method Parameter Injection (Recommended)

**✅ Recommended:** Use method parameter injection to avoid singleton lifetime issues:

```csharp
public class OrderHandler
{
    // No constructor dependencies - handler can be singleton safely

    // First parameter is always the message
    // Additional parameters are resolved from DI per invocation
    public async Task<Result<Order>> Handle(
        CreateOrderCommand command,           // Message parameter
        IOrderRepository repository,          // Fresh instance per call
        ILogger<OrderHandler> logger,         // Fresh instance per call
        CancellationToken cancellationToken)  // Automatically provided
    {
        logger.LogInformation("Processing order creation");
        return await repository.CreateAsync(command.ToOrder(), cancellationToken);
    }
}
```

### Benefits of Method Parameter Injection

1. **No lifetime conflicts** - Dependencies resolved per invocation
2. **Automatic cancellation support** - `CancellationToken` provided automatically
3. **Cleaner testing** - Easy to mock individual method calls
4. **Better performance** - Handler can be singleton, dependencies fresh when needed

### Common Injectable Services

These services are commonly injected into handler methods:

- `ILogger<T>` - For logging
- `CancellationToken` - For cancellation support
- `IServiceProvider` - For service location
- Repository interfaces
- Business service interfaces
- Configuration objects

## Automatic DI Scope Management

Foundatio.Mediator automatically manages dependency injection scopes to ensure proper lifetime handling of scoped services like DbContext.

### Root Handler Invocation Creates a Scope

When you invoke a handler from a root mediator call (outside of another handler), a new DI scope is automatically created:

```csharp
// This creates a new DI scope
var result = await mediator.InvokeAsync<Order>(new CreateOrderCommand("test@example.com"));

// The scope is disposed when the operation completes
```

### Nested Operations Share the Same Scope

All nested handler invocations within the same logical operation share the same DI scope:

- **Cascading messages** - Events published via tuple returns use the same scope
- **Manual handler calls** - Calling other handlers from within a handler
- **Manual publishing** - Publishing events from within a handler
- **Middleware operations** - All middleware in the pipeline

```csharp
public class OrderHandler
{
    public async Task<(Result<Order>, OrderCreated, EmailNotification)> Handle(
        CreateOrderCommand command,
        IOrderRepository repository,   // Scoped - same instance throughout operation
        IMediator mediator,           // Can call other handlers in same scope
        CancellationToken cancellationToken)
    {
        // This repository instance will be shared with all nested operations
        var order = await repository.CreateAsync(command.ToOrder(), cancellationToken);

        // These nested operations will use the SAME DI scope:
        // 1. Manual handler call
        await mediator.InvokeAsync(new UpdateInventoryCommand(order.ProductId), cancellationToken);

        // 2. Manual event publishing
        await mediator.PublishAsync(new OrderValidated(order.Id), cancellationToken);

        // 3. Cascading events (via tuple return) - also use same scope
        return (
            Result<Order>.Created(order),
            new OrderCreated(order.Id, order.Email),      // Uses same scope
            new EmailNotification(order.Email, "Order")   // Uses same scope
        );
    }
}

public class InventoryHandler
{
    public async Task Handle(
        UpdateInventoryCommand command,
        IOrderRepository repository)  // SAME INSTANCE as in OrderHandler!
    {
        // This shares the DbContext/repository instance with the parent handler
        var order = await repository.GetByIdAsync(command.OrderId);
        // ... update inventory
    }
}
```

### Benefits of Shared Scope

**🔄 Consistent Data Access:**

- All handlers in the same operation see the same data
- DbContext change tracking works across nested handlers
- Transactions can span multiple handlers

**⚡ Performance:**

- Expensive scoped services created once per operation
- Connection pooling is more efficient
- Reduced service resolution overhead

**🛡️ Data Integrity:**

- Natural unit of work boundaries
- Easier to maintain consistency across operations
- Proper cleanup when operation completes

## Middleware Lifetime

Middleware instances are cached and reused by default:

```csharp
public class LoggingMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public static void Before(object message, ILogger<LoggingMiddleware> logger)
    {
        logger.LogInformation("Handling {MessageType}", message.GetType().Name);
    }
}
```

### Explicit Middleware Registration

Control middleware lifetime by registering in DI:

```csharp
builder.Services.AddMediator();

// Register middleware with specific lifetime
builder.Services.AddScoped<ValidationMiddleware>();
builder.Services.AddSingleton<LoggingMiddleware>();
```

## Scoped Services Example

Here's a complete example showing scoped services in a web application:

```csharp
// Startup.cs or Program.cs
builder.Services.AddMediator();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Handler using scoped services
public class OrderHandler
{
    public async Task<Result<Order>> Handle(
        CreateOrderCommand command,
        IOrderRepository repository,
        IEmailService emailService,
        ILogger<OrderHandler> logger)
    {
        logger.LogInformation("Creating order for {Email}", command.Email);

        var order = new Order
        {
            Email = command.Email,
            Amount = command.Amount
        };

        await repository.SaveAsync(order);
        await emailService.SendConfirmationAsync(order);

        return order;
    }
}

// Controller
[ApiController]
public class OrderController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrderController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderCommand command)
    {
        var result = await _mediator.Invoke(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
```

## Service Location Pattern

While constructor injection is preferred, you can access the service provider directly:

```csharp
public class OrderHandler
{
    public async Task<Result<Order>> Handle(
        CreateOrderCommand command,
        IServiceProvider serviceProvider)
    {
        var repository = serviceProvider.GetRequiredService<IOrderRepository>();
        var logger = serviceProvider.GetRequiredService<ILogger<OrderHandler>>();

        logger.LogInformation("Creating order");
        return await repository.CreateAsync(command.ToOrder());
    }
}
```

## Best Practices

### 1. Prefer Method Injection for Most Scenarios

```csharp
// ✅ RECOMMENDED: Method injection - no lifetime issues
public class OrderHandler
{
    public async Task<Result<Order>> Handle(
        CreateOrderCommand command,
        IOrderRepository repository,  // Fresh per call
        ILogger<OrderHandler> logger) // Fresh per call
    {
        logger.LogInformation("Creating order");
        return await repository.CreateAsync(command.ToOrder());
    }
}
```

### 2. Use Constructor Injection Only with Proper Registration

```csharp
// ✅ SAFE: Constructor injection with explicit lifetime registration
public class OrderHandler
{
    private readonly IOrderRepository _repository;

    public OrderHandler(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Order>> Handle(CreateOrderCommand command)
    {
        return await _repository.CreateAsync(command.ToOrder());
    }
}

// Must register with matching lifetime:
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<OrderHandler>(); // Matches repository lifetime
```

### 3. Static Methods Are Singleton-Safe

```csharp
// ✅ EXCELLENT: Static methods with method injection
public static class OrderHandler
{
    public static async Task<Result<Order>> Handle(
        CreateOrderCommand command,
        IOrderRepository repository,
        ILogger<OrderHandler> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating order");
        return await repository.CreateAsync(command.ToOrder(), cancellationToken);
    }
}
```

### 4. Avoid Service Location

```csharp
// ❌ AVOID: Service location pattern
public async Task<Result> Handle(CreateOrderCommand command, IServiceProvider provider)
{
    var service = provider.GetService<IOrderService>(); // Don't do this
}

// ✅ PREFER: Direct injection
public async Task<Result> Handle(CreateOrderCommand command, IOrderService service)
{
    // Use service directly
}
```

## Integration with ASP.NET Core

The mediator integrates seamlessly with ASP.NET Core's built-in DI:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add framework services
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add application services
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Add mediator - discovers handlers automatically
builder.Services.AddMediator();

var app = builder.Build();
```

This setup ensures that all your handlers have access to the same scoped services as your controllers, maintaining consistency across your application's request pipeline.
