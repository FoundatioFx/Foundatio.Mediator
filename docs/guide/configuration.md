# Configuration Options

Foundatio Mediator provides two types of configuration: **compile-time configuration** via MSBuild properties that control source generator behavior, and **runtime configuration** via the `AddMediator()` method that controls mediator behavior.

## Compile-Time Configuration (MSBuild Properties)

These properties control the source generator at compile time and affect code generation:

### Available MSBuild Properties

```xml
<PropertyGroup>
    <!-- Default lifetime for handlers that don't specify one (see per-handler lifetime below) -->
    <MediatorDefaultHandlerLifetime>Scoped</MediatorDefaultHandlerLifetime>

    <!-- Default lifetime for middleware that don't specify one (see per-middleware lifetime below) -->
    <MediatorDefaultMiddlewareLifetime>Scoped</MediatorDefaultMiddlewareLifetime>

    <!-- Control interceptor generation (default: false) -->
    <MediatorDisableInterceptors>true</MediatorDisableInterceptors>

    <!-- Disable OpenTelemetry integration (default: false) -->
    <MediatorDisableOpenTelemetry>true</MediatorDisableOpenTelemetry>

    <!-- Disable conventional handler discovery (default: false) -->
    <MediatorDisableConventionalDiscovery>true</MediatorDisableConventionalDiscovery>
</PropertyGroup>
```

### Property Details

**`MediatorDefaultHandlerLifetime`**

- **Values:** `Scoped`, `Transient`, `Singleton`, `None`
- **Default:** `None` (handlers not auto-registered)
- **Effect:** Automatically registers all discovered handlers with the specified DI lifetime, unless overridden by `[Handler(Lifetime = ...)]` attribute
- **Note:** When set to `None`, handlers are not automatically registered in DI unless they specify a lifetime via attribute

**`MediatorDefaultMiddlewareLifetime`**

- **Values:** `Scoped`, `Transient`, `Singleton`, `None`
- **Default:** `None` (middleware not auto-registered, uses internal caching)
- **Effect:** Automatically registers all discovered middleware with the specified DI lifetime, unless overridden by `[Middleware(Lifetime = ...)]` attribute
- **Note:** When set to `None`, middleware instances are cached internally (effectively singleton behavior). Setting a lifetime enables proper DI lifecycle management.

### Per-Handler Lifetime Override

Individual handlers can override the project-level default lifetime using the `[Handler]` attribute:

```csharp
// Uses project-level MediatorDefaultHandlerLifetime
public class DefaultLifetimeHandler
{
    public Task HandleAsync(MyMessage msg) => Task.CompletedTask;
}

// Explicitly registered as Singleton (overrides project default)
[Handler(Lifetime = MediatorLifetime.Singleton)]
public class CachedDataHandler
{
    public CachedData Handle(GetCachedData query) => _cache.Get();
}

// Explicitly registered as Transient
[Handler(Lifetime = MediatorLifetime.Transient)]
public class StatelessHandler
{
    public void Handle(FireAndForgetEvent evt) { }
}

// Combined with Order for publish scenarios
[Handler(Order = 1, Lifetime = MediatorLifetime.Scoped)]
public class FirstScopedHandler
{
    public void Handle(MyEvent evt) { }
}
```

**Available `MediatorLifetime` values:**
- `MediatorLifetime.Default` - Use project-level `MediatorDefaultHandlerLifetime`
- `MediatorLifetime.Transient` - New instance per request
- `MediatorLifetime.Scoped` - Same instance within a scope
- `MediatorLifetime.Singleton` - Single instance for application lifetime

### Per-Middleware Lifetime Override

Individual middleware can override the project-level default lifetime using the `[Middleware]` attribute:

```csharp
// Uses project-level MediatorDefaultMiddlewareLifetime
public class DefaultLifetimeMiddleware
{
    public void Before(object msg) { }
}

// Explicitly registered as Singleton (overrides project default)
[Middleware(Lifetime = MediatorLifetime.Singleton)]
public class CachingMiddleware
{
    private readonly IMemoryCache _cache;
    public CachingMiddleware(IMemoryCache cache) => _cache = cache;

    public void Before(object msg) { /* caching logic */ }
}

// Explicitly registered as Scoped
[Middleware(Lifetime = MediatorLifetime.Scoped)]
public class RequestScopedMiddleware
{
    private readonly IHttpContextAccessor _httpContext;
    public RequestScopedMiddleware(IHttpContextAccessor httpContext) => _httpContext = httpContext;

    public void Before(object msg) { /* request-scoped logic */ }
}

// Combined with Order for execution ordering
[Middleware(Order = 1, Lifetime = MediatorLifetime.Transient)]
public class FirstTransientMiddleware
{
    public void Before(object msg) { }
}
```

**`MediatorDisableInterceptors`**

- **Values:** `true`, `false`
- **Default:** `false`
- **Effect:** When `true`, disables C# interceptor generation and forces DI-based dispatch for all calls
- **Use Case:** Debugging, cross-assembly calls, or when interceptors are not supported

**`MediatorDisableOpenTelemetry`**

- **Values:** `true`, `false`
- **Default:** `false`
- **Effect:** When `true`, disables OpenTelemetry integration code generation
- **Use Case:** Reduce generated code size when telemetry is not needed

**`MediatorDisableConventionalDiscovery`**

- **Values:** `true`, `false`
- **Default:** `false`
- **Effect:** When `true`, disables convention-based handler discovery (class names ending with `Handler` or `Consumer`). Only handlers that implement `IHandler` interface or have the `[Handler]` attribute will be discovered.
- **Use Case:** Explicit control over which classes are treated as handlers, avoiding accidental handler discovery

### Example .csproj Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>

    <!-- Compile-time configuration -->
    <MediatorDefaultHandlerLifetime>Scoped</MediatorDefaultHandlerLifetime>
    <MediatorDefaultMiddlewareLifetime>Scoped</MediatorDefaultMiddlewareLifetime>
    <MediatorDisableInterceptors>false</MediatorDisableInterceptors>
    <MediatorDisableOpenTelemetry>true</MediatorDisableOpenTelemetry>
    <MediatorDisableConventionalDiscovery>false</MediatorDisableConventionalDiscovery>
  </PropertyGroup>

  <PackageReference Include="Foundatio.Mediator" Version="1.0.0" />

</Project>
```

## Runtime Configuration (AddMediator Method)

### Default Setup

The simplest configuration automatically discovers handlers and registers the mediator:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Default configuration - discovers all handlers
builder.Services.AddMediator();

var app = builder.Build();
```

### Configuration with Builder

```csharp
builder.Services.AddMediator(cfg => cfg
    .AddAssembly(typeof(Program))
    .SetMediatorLifetime(ServiceLifetime.Scoped)
    .UseForeachAwaitPublisher());
```

## Mediator Configuration Options

### MediatorConfiguration Class

```csharp
public class MediatorConfiguration {
    public List<Assembly>? Assemblies { get; set; }
    public ServiceLifetime MediatorLifetime { get; set; } = ServiceLifetime.Scoped;
    public INotificationPublisher NotificationPublisher { get; set; } = new ForeachAwaitPublisher();
}
```

### Notification Publishers

Foundatio Mediator provides three built-in notification publishers that control how `PublishAsync` dispatches messages to multiple handlers:

| Publisher | Behavior | Use Case |
|-----------|----------|----------|
| `ForeachAwaitPublisher` | Invokes handlers **sequentially**, one at a time (default) | Predictable ordering, easier debugging |
| `TaskWhenAllPublisher` | Invokes all handlers **concurrently** and waits for all to complete | Maximum throughput when handlers are independent |
| `FireAndForgetPublisher` | Fires all handlers **in parallel without waiting** | Background events where you don't need to wait for completion |

**Example:**

```csharp
// Use parallel execution with await
builder.Services.AddMediator(cfg => cfg
    .UseNotificationPublisher(new TaskWhenAllPublisher()));

// Fire and forget - returns immediately
builder.Services.AddMediator(cfg => cfg
    .UseNotificationPublisher(new FireAndForgetPublisher()));
```

> ⚠️ **Warning:** `FireAndForgetPublisher` swallows exceptions and handlers may outlive the HTTP request. Use with caution and ensure proper error handling within your handlers.

## Handler Discovery Configuration

### Automatic Discovery

By default, handlers are discovered automatically in the calling assembly:

```csharp
// Discovers handlers in the current assembly
builder.Services.AddMediator();
```

### Custom Assembly Discovery

```csharp
builder.Services.AddMediator(cfg => cfg
    .AddAssembly(typeof(OrderHandler).Assembly)
    .AddAssembly(typeof(NotificationHandler).Assembly));
```

### Handler Registration

Register handlers explicitly to control lifetime (otherwise first created instance is cached):

```csharp
builder.Services.AddScoped<OrderHandler>();
builder.Services.AddTransient<EmailHandler>();
```

## MSBuild Configuration

Disable interceptors if you need to force DI dispatch:

```xml
<PropertyGroup>
    <MediatorDisableInterceptors>true</MediatorDisableInterceptors>
</PropertyGroup>
```

## Dependency Injection Integration

`AddMediator` registers `IMediator` with configured lifetime and invokes generated handler module registration methods. It does not register handler classes; register them yourself to control lifetime.

Custom mediator implementations can be supplied by registering your own `IMediator`.

## Environment-Specific Configuration

Adjust registration or add middleware conditionally using standard ASP.NET Core environment checks; there are no built-in flags for tracing or throw-on-not-found.

## Logging

Standard ASP.NET Core logging works; add logging middleware for per-message logs.

### Custom Logging Middleware

```csharp
public class DetailedLoggingMiddleware
{
    public static (DateTime StartTime, string CorrelationId) Before(
        object message,
        ILogger<DetailedLoggingMiddleware> logger)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;

        logger.LogInformation(
            "[{CorrelationId}] Starting {MessageType} at {StartTime}",
            correlationId, message.GetType().Name, startTime);

        return (startTime, correlationId);
    }

    public static void After(
        object message,
        object? response,
        DateTime startTime,
        string correlationId,
        ILogger<DetailedLoggingMiddleware> logger)
    {
        var duration = DateTime.UtcNow - startTime;

        logger.LogInformation(
            "[{CorrelationId}] Completed {MessageType} in {Duration}ms",
            correlationId, message.GetType().Name, duration.TotalMilliseconds);
    }
}
```
