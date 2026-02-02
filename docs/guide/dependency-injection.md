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

## Mediator Lifetime and Scoped Services

> **Key Rule:** If your handlers use scoped or transient services (like `DbContext`), you must register the mediator as scoped.

By default, the mediator is registered as a **singleton**, which means it captures the root `IServiceProvider` at construction time. This causes scoped services to be resolved from the root provider, effectively making them singletons.

### Register Mediator as Scoped

```csharp
// Required when using scoped/transient services in handlers
services.AddMediator(b => b.SetMediatorLifetime(ServiceLifetime.Scoped));
```

This ensures each DI scope (HTTP request, background job, test) gets its own mediator instance that resolves services from the correct scope.

### When to Use Scoped Mediator

| Scenario | Need Scoped Mediator? |
| -------- | --------------------- |
| Handlers use `DbContext` or other scoped services | **Yes** |
| Handlers use transient services | **Yes** |
| Handlers only use singletons or no DI | No (default is fine) |

## Middleware Lifetime

Middleware lifetime follows the same rules as handler lifetime:

| Lifetime | Behavior |
|----------|----------|
| **Scoped** | Resolved from DI on every invocation |
| **Transient** | Resolved from DI on every invocation |
| **Singleton** | Resolved from DI on every invocation (DI handles caching) |
| **None/Default** (no constructor deps) | Created once with `new()` and cached in static field |
| **None/Default** (with constructor deps) | Created once with `ActivatorUtilities.CreateInstance` and cached |

## Handler Lifetime Management

### Lifetime Behavior Summary

| Lifetime | Behavior |
|----------|----------|
| **Scoped** | Resolved from DI on every invocation |
| **Transient** | Resolved from DI on every invocation |
| **Singleton** | Resolved from DI on every invocation (DI handles caching) |
| **None/Default** (no constructor deps) | Created once with `new()` and cached in static field |
| **None/Default** (with constructor deps) | Created once with `ActivatorUtilities.CreateInstance` and cached |

### Important: Default Behavior When Lifetime Not Specified

If you don't explicitly set a lifetime (via `[Handler(Lifetime = ...)]` or `MediatorDefaultHandlerLifetime`), the handler instance will be cached:

- **No constructor parameters**: Instantiated with `new()` and cached forever
- **With constructor parameters**: Created via `ActivatorUtilities.CreateInstance` and cached - constructor dependencies are resolved once and reused

```csharp
// WARNING: This handler is cached - dependencies resolved once!
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

### Explicit Lifetime Always Uses DI

When you explicitly set a lifetime (`Scoped`, `Transient`, or `Singleton`), the handler is **always resolved from the DI container**:

```csharp
// Singleton - resolved from DI, DI handles the singleton caching
[Handler(Lifetime = MediatorLifetime.Singleton)]
public class CacheHandler { }

// Scoped - resolved from DI on each invocation
[Handler(Lifetime = MediatorLifetime.Scoped)]
public class OrderHandler { }
```

This ensures proper test isolation - each test with its own DI container gets its own handler instances.

### Controlling Lifetime

There are two ways to control handler lifetime:

**1. Using the `[Handler]` attribute:**

```csharp
[Handler(Lifetime = MediatorLifetime.Scoped)]
public class OrderHandler { /* ... */ }
```

**2. Using `MediatorDefaultHandlerLifetime` MSBuild property** (see below)

### Automatic Handler Registration with MSBuild

You can automatically register all handlers in your project with a specific lifetime using the `MediatorDefaultHandlerLifetime` MSBuild property:

```xml
<PropertyGroup>
    <MediatorDefaultHandlerLifetime>Scoped</MediatorDefaultHandlerLifetime>
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
    <MediatorDefaultHandlerLifetime>Scoped</MediatorDefaultHandlerLifetime>
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
// Uses project-level MediatorDefaultHandlerLifetime
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
- `MediatorLifetime.Default` - Use project-level `MediatorDefaultHandlerLifetime`
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

### Default Behavior (No Explicit Lifetime)

When no lifetime is specified, middleware instances are cached:

```csharp
// No explicit lifetime - cached with new() since no constructor deps
public class SimpleMiddleware
{
    public void Before(object message) { /* ... */ }
}

// No explicit lifetime - cached via ActivatorUtilities since it has constructor deps
public class LoggingMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger; // Resolved once and cached!
    }

    public void Before(object message)
    {
        _logger.LogInformation("Handling {MessageType}", message.GetType().Name);
    }
}
```

### Explicit Lifetime with [Middleware] Attribute

Use the `[Middleware]` attribute to explicitly control lifetime:

```csharp
// Resolved from DI on every invocation - DI handles singleton caching
[Middleware(Lifetime = MediatorLifetime.Singleton)]
public class LoggingMiddleware { /* ... */ }

// Resolved from DI on every invocation
[Middleware(Lifetime = MediatorLifetime.Scoped)]
public class ValidationMiddleware { /* ... */ }
```

### Project-Level Default with MSBuild

Set a default lifetime for all middleware using `MediatorDefaultMiddlewareLifetime`:

```xml
<PropertyGroup>
    <MediatorDefaultMiddlewareLifetime>Scoped</MediatorDefaultMiddlewareLifetime>
</PropertyGroup>
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

// Add mediator - discovers handlers automatically uses scoped Mediator lifetime to be compatible with any scoped/transient services
builder.Services.AddMediator(b => b.SetMediatorLifetime(ServiceLifetime.Scoped));

var app = builder.Build();
```

This setup ensures that all your handlers have access to the same scoped services as your controllers, maintaining consistency across your application's request pipeline.
