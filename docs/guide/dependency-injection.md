# Dependency Injection

Foundatio.Mediator seamlessly integrates with Microsoft.Extensions.DependencyInjection to provide powerful dependency injection capabilities for both handlers and middleware.

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

### Important: Handlers are Singleton by Default

**⚠️ Critical:** Handlers are treated as **singletons** by default. Constructor dependencies are resolved once and shared across all invocations.

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

When a handler is needed:

1. **If registered in DI**: The container creates and manages the instance
2. **If not registered**: `ActivatorUtilities.CreateInstance` creates the handler as singleton

### Explicit Handler Registration for Lifetime Control

To avoid singleton issues with scoped dependencies, register handlers explicitly:

```csharp
builder.Services.AddMediator();

// Register handlers with proper lifetimes to match their dependencies
builder.Services.AddScoped<OrderHandler>();    // Matches DbContext scope
builder.Services.AddTransient<EmailHandler>(); // New instance each time
builder.Services.AddSingleton<CacheHandler>(); // Truly singleton
```

## Constructor Injection (Use with Caution)

**⚠️ Warning:** Constructor injection creates singleton handlers unless explicitly registered with different lifetime.

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
