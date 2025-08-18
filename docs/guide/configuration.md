# Configuration Options

Foundatio Mediator provides various configuration options to customize behavior, performance characteristics, and integration with your application.

## Basic Configuration

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
    public INotificationPublisher NotificationPublisher { get; set; } = new TaskWhenAllPublisher();
}
```

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

## Monitoring and Telemetry

Add custom middleware for tracing/metrics as needed.

## Advanced Configuration Scenarios

Multi-tenancy, feature flags, circuit breakers etc. can be implemented via custom middleware or by wrapping `IMediator`.

## Health Checks

Implement a simple ping handler and invoke it in a custom health check if desired.

Configuration surface is intentionally small; use DI and middleware for advanced scenarios.
