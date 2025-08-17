# Configuration Options

Foundatio.Mediator provides various configuration options to customize behavior, performance characteristics, and integration with your application.

## Basic Configuration

### Default Setup

The simplest configuration automatically discovers handlers and registers the mediator:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Default configuration - discovers all handlers
builder.Services.AddMediator();

var app = builder.Build();
```

### Configuration with Options

```csharp
builder.Services.AddMediator(options =>
{
    options.DefaultHandlerLifetime = ServiceLifetime.Scoped;
    options.EnableActivitySource = true;
    options.ThrowOnHandlerNotFound = false;
});
```

## Mediator Configuration Options

### MediatorConfiguration Class

```csharp
public class MediatorConfiguration
{
    /// <summary>
    /// Default lifetime for handlers when registered in DI
    /// </summary>
    public ServiceLifetime DefaultHandlerLifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Whether to enable OpenTelemetry activity source
    /// </summary>
    public bool EnableActivitySource { get; set; } = true;

    /// <summary>
    /// Whether to throw exception when no handler found
    /// </summary>
    public bool ThrowOnHandlerNotFound { get; set; } = true;

    /// <summary>
    /// Maximum time to wait for async operations
    /// </summary>
    public TimeSpan AsyncTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Custom handler discovery assemblies
    /// </summary>
    public Assembly[] HandlerAssemblies { get; set; } = Array.Empty<Assembly>();
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
builder.Services.AddMediator(options =>
{
    // Discover handlers in specific assemblies
    options.HandlerAssemblies = new[]
    {
        typeof(OrderHandler).Assembly,        // Business logic assembly
        typeof(NotificationHandler).Assembly, // Infrastructure assembly
        Assembly.GetExecutingAssembly()       // Current assembly
    };
});
```

### Selective Handler Registration

```csharp
builder.Services.AddMediator();

// Register specific handlers with custom lifetimes
builder.Services.AddScoped<OrderHandler>();
builder.Services.AddSingleton<CacheHandler>();
builder.Services.AddTransient<EmailHandler>();
```

## MSBuild Configuration

### Interceptor Control

Control interceptor generation at build time:

```xml
<PropertyGroup>
  <!-- Disable interceptors (falls back to DI resolution) -->
  <MediatorDisableInterceptors>true</MediatorDisableInterceptors>

  <!-- Control source generator verbosity -->
  <MediatorGeneratorVerbose>true</MediatorGeneratorVerbose>

  <!-- Specify custom handler naming patterns -->
  <MediatorHandlerSuffix>Handler;Consumer;Processor</MediatorHandlerSuffix>
</PropertyGroup>
```

### Source Generator Options

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="MediatorDisableInterceptors" />
  <CompilerVisibleProperty Include="MediatorGeneratorVerbose" />
</ItemGroup>

<PropertyGroup>
  <!-- Advanced generator settings -->
  <MediatorDisableInterceptors Condition="'$(Configuration)' == 'Debug'">true</MediatorDisableInterceptors>
  <MediatorGenerateNullableAnnotations>true</MediatorGenerateNullableAnnotations>
</PropertyGroup>
```

## Dependency Injection Integration

### Service Registration Options

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        Action<MediatorConfiguration>? configure = null)
    {
        var config = new MediatorConfiguration();
        configure?.Invoke(config);

        services.AddSingleton(config);
        services.AddSingleton<IMediator, Mediator>();

        // Auto-discover and register handlers
        DiscoverAndRegisterHandlers(services, config);

        return services;
    }
}
```

### Custom Mediator Implementation

```csharp
public class CustomMediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MediatorConfiguration _config;
    private readonly ILogger<CustomMediator> _logger;

    public CustomMediator(
        IServiceProvider serviceProvider,
        MediatorConfiguration config,
        ILogger<CustomMediator> logger)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = logger;
    }

    public async Task<TResponse> InvokeAsync<TResponse>(
        IRequest<TResponse> message,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartActivity(message);

        try
        {
            return await InvokeHandlerAsync(message, cancellationToken);
        }
        catch (Exception ex) when (_config.ThrowOnHandlerNotFound)
        {
            _logger.LogError(ex, "Handler execution failed for {MessageType}", typeof(TResponse).Name);
            throw;
        }
    }
}

// Register custom mediator
builder.Services.AddSingleton<IMediator, CustomMediator>();
```

## Environment-Specific Configuration

### Development Configuration

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddMediator(options =>
    {
        options.EnableActivitySource = true;           // Enable tracing
        options.ThrowOnHandlerNotFound = true;         // Fail fast in dev
        options.DefaultHandlerLifetime = ServiceLifetime.Transient; // Fresh instances
    });
}
```

### Production Configuration

```csharp
if (builder.Environment.IsProduction())
{
    builder.Services.AddMediator(options =>
    {
        options.EnableActivitySource = true;           // Enable monitoring
        options.ThrowOnHandlerNotFound = false;        // Graceful degradation
        options.DefaultHandlerLifetime = ServiceLifetime.Scoped; // Efficient lifetime
        options.AsyncTimeout = TimeSpan.FromSeconds(30); // Reasonable timeout
    });
}
```

### Testing Configuration

```csharp
// In test projects
public class TestStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMediator(options =>
        {
            options.EnableActivitySource = false;      // Disable tracing overhead
            options.ThrowOnHandlerNotFound = true;     // Catch missing handlers
            options.DefaultHandlerLifetime = ServiceLifetime.Transient; // Isolated tests
        });

        // Mock external dependencies
        services.AddSingleton<IEmailService, MockEmailService>();
        services.AddSingleton<IPaymentService, MockPaymentService>();
    }
}
```

## Logging Configuration

### Built-in Logging

```csharp
builder.Services.AddMediator();
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();

    // Configure mediator-specific logging
    logging.AddFilter("Foundatio.Mediator", LogLevel.Information);
});
```

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

### OpenTelemetry Integration

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Foundatio.Mediator");
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddConsoleExporter();
    });

builder.Services.AddMediator(options =>
{
    options.EnableActivitySource = true; // Enable OpenTelemetry tracing
});
```

### Custom Activity Configuration

```csharp
public class TelemetryMiddleware
{
    public static Activity? Before(object message)
    {
        var activity = MediatorActivitySource.StartActivity($"Handle {message.GetType().Name}");
        activity?.SetTag("message.type", message.GetType().FullName);
        activity?.SetTag("message.size", JsonSerializer.Serialize(message).Length);
        return activity;
    }

    public static void After(object message, object? response, Activity? activity)
    {
        activity?.SetTag("response.type", response?.GetType().FullName ?? "void");
        activity?.SetTag("success", response != null);
        activity?.Dispose();
    }
}
```

## Advanced Configuration Scenarios

### Multi-Tenant Configuration

```csharp
public class TenantAwareMediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITenantContext _tenantContext;

    public async Task<TResponse> InvokeAsync<TResponse>(
        IRequest<TResponse> message,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenant.Id;

        // Use tenant-specific service scope
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Invoke(message, cancellationToken);
    }
}
```

### Feature Flag Integration

```csharp
public class FeatureFlagMiddleware
{
    public static HandlerResult Before(object message, IFeatureManager featureManager)
    {
        var messageType = message.GetType().Name;
        var featureFlag = $"Enable{messageType}";

        if (!featureManager.IsEnabledAsync(featureFlag).Result)
        {
            return HandlerResult.ShortCircuit(
                Result.Failed($"Feature {featureFlag} is disabled"));
        }

        return HandlerResult.Continue();
    }
}
```

### Circuit Breaker Configuration

```csharp
public class CircuitBreakerMiddleware
{
    private static readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers = new();

    public static HandlerResult Before(object message)
    {
        var messageType = message.GetType().Name;
        var circuitBreaker = _circuitBreakers.GetOrAdd(messageType,
            _ => new CircuitBreaker(
                failureThreshold: 5,
                recoveryTime: TimeSpan.FromMinutes(1)));

        if (circuitBreaker.IsOpen)
        {
            return HandlerResult.ShortCircuit(
                Result.Failed("Circuit breaker is open"));
        }

        return HandlerResult.Continue();
    }

    public static void Finally(object message, Exception? exception)
    {
        var messageType = message.GetType().Name;
        if (_circuitBreakers.TryGetValue(messageType, out var circuitBreaker))
        {
            if (exception != null)
                circuitBreaker.RecordFailure();
            else
                circuitBreaker.RecordSuccess();
        }
    }
}
```

## Configuration Validation

### Startup Validation

```csharp
public class MediatorConfigurationValidator : IValidateOptions<MediatorConfiguration>
{
    public ValidateOptionsResult Validate(string name, MediatorConfiguration options)
    {
        var errors = new List<string>();

        if (options.AsyncTimeout <= TimeSpan.Zero)
        {
            errors.Add("AsyncTimeout must be greater than zero");
        }

        if (options.HandlerAssemblies.Any(a => a == null))
        {
            errors.Add("HandlerAssemblies cannot contain null values");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

// Register validator
builder.Services.AddSingleton<IValidateOptions<MediatorConfiguration>, MediatorConfigurationValidator>();
```

### Health Checks

```csharp
public class MediatorHealthCheck : IHealthCheck
{
    private readonly IMediator _mediator;

    public MediatorHealthCheck(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Test with a simple ping message
            await _mediator.Invoke(new HealthCheckQuery(), cancellationToken);
            return HealthCheckResult.Healthy("Mediator is responding");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Mediator failed health check", ex);
        }
    }
}

// Register health check
builder.Services.AddHealthChecks()
    .AddCheck<MediatorHealthCheck>("mediator");
```

## Configuration Best Practices

### 1. Environment-Specific Settings

```csharp
// Use configuration providers
builder.Services.Configure<MediatorConfiguration>(
    builder.Configuration.GetSection("Mediator"));
```

### 2. Validate Configuration Early

```csharp
// Validate at startup
var config = builder.Configuration.GetSection("Mediator").Get<MediatorConfiguration>();
if (config?.AsyncTimeout <= TimeSpan.Zero)
{
    throw new InvalidOperationException("Invalid mediator configuration");
}
```

### 3. Document Configuration Options

```json
{
  "Mediator": {
    "DefaultHandlerLifetime": "Scoped",
    "EnableActivitySource": true,
    "ThrowOnHandlerNotFound": false,
    "AsyncTimeout": "00:05:00"
  }
}
```

### 4. Use Typed Configuration

```csharp
public class MediatorOptions
{
    public const string SectionName = "Mediator";

    public ServiceLifetime DefaultHandlerLifetime { get; set; } = ServiceLifetime.Transient;
    public bool EnableActivitySource { get; set; } = true;
    public bool ThrowOnHandlerNotFound { get; set; } = true;
    public TimeSpan AsyncTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

// Register with IOptions pattern
builder.Services.Configure<MediatorOptions>(
    builder.Configuration.GetSection(MediatorOptions.SectionName));
```

This comprehensive configuration system allows you to customize Foundatio.Mediator's behavior for different environments, requirements, and integration scenarios while maintaining clean, maintainable code.
