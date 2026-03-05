# Configuration Options

::: tip You Probably Don't Need This
Foundatio Mediator works out of the box with sensible defaults — most projects never need to configure anything beyond `services.AddMediator()`. Only reach for the options below when you want to change a specific default behavior.
:::

Foundatio Mediator provides two types of configuration: **compile-time configuration** via the `[assembly: MediatorConfiguration]` attribute that controls source generator behavior, and **runtime configuration** via the `AddMediator()` method that controls mediator behavior.

## Compile-Time Configuration (Assembly Attribute)

All source generator settings—handler discovery, lifetimes, interceptors, telemetry, and endpoint generation—are configured through a single assembly-level attribute:

```csharp
using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    HandlerLifetime = MediatorLifetime.Scoped,
    EndpointDiscovery = EndpointDiscovery.All,
    EndpointRoutePrefix = "api"
)]
```

### Property Details

**`HandlerLifetime`** (`MediatorLifetime` enum)

- **Values:** `Default`, `Transient`, `Scoped`, `Singleton`
- **Default:** `Default` (handlers use internal caching)
- **Effect:** Registers all discovered handlers with the specified DI lifetime, unless overridden by `[Handler(Lifetime = ...)]` attribute
- **Behavior by value:**
  - `Scoped`/`Transient`/`Singleton`: Always resolved from DI on every invocation
  - `Default`: Handlers are cached internally (no constructor deps → `new()`, with constructor deps → `ActivatorUtilities.CreateInstance`)

**`MiddlewareLifetime`** (`MediatorLifetime` enum)

- **Values:** `Default`, `Transient`, `Scoped`, `Singleton`
- **Default:** `Default` (middleware uses internal caching)
- **Effect:** Registers all discovered middleware with the specified DI lifetime, unless overridden by `[Middleware(Lifetime = ...)]` attribute
- **Behavior by value:**
  - `Scoped`/`Transient`/`Singleton`: Always resolved from DI on every invocation
  - `Default`: Middleware is cached internally (no constructor deps → `new()`, with constructor deps → `ActivatorUtilities.CreateInstance`)

### Per-Handler Lifetime Override

Individual handlers can override the project-level default lifetime using the `[Handler]` attribute:

```csharp
// Uses project-level HandlerLifetime from [assembly: MediatorConfiguration]
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
- `MediatorLifetime.Default` - Use project-level `HandlerLifetime` from `[assembly: MediatorConfiguration]`
- `MediatorLifetime.Transient` - New instance per request
- `MediatorLifetime.Scoped` - Same instance within a scope
- `MediatorLifetime.Singleton` - Single instance for application lifetime

### Per-Middleware Lifetime Override

Individual middleware can override the project-level default lifetime using the `[Middleware]` attribute:

```csharp
// Uses project-level MiddlewareLifetime from [assembly: MediatorConfiguration]
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

**`DisableInterceptors`** (`bool`)

- **Default:** `false`
- **Effect:** When `true`, disables C# interceptor generation and forces DI-based dispatch for all calls
- **Use Case:** Debugging, cross-assembly calls, or when interceptors are not supported

**`DisableOpenTelemetry`** (`bool`)

- **Default:** `false`
- **Effect:** When `true`, disables OpenTelemetry integration code generation
- **Use Case:** Reduce generated code size when telemetry is not needed

**`DisableAuthorization`** (`bool`)

- **Default:** `false`
- **Effect:** When `true`, disables all generated authorization checks in handler code and prevents registration of authorization-related services (`IHttpContextAccessor`, `HttpContextAuthorizationContextProvider`, `IAuthorizationContextProvider`). `[HandlerAuthorize]` attributes are ignored for inline mediator call auth checks. Endpoint-level `.RequireAuthorization()` is **not** affected.
- **Use Case:** Projects that don't need mediator-level authorization, or projects that want to avoid `IHttpContextAccessor` being automatically registered

**`HandlerDiscovery`** (`HandlerDiscovery` enum)

- **Values:** `All`, `Explicit`
- **Default:** `All`
- **Effect:** Controls how handlers are discovered at compile time
  - `All`: Convention-based discovery (class names ending with `Handler` or `Consumer`) plus `IHandler` interface and `[Handler]` attribute
  - `Explicit`: Only handlers that implement `IHandler` interface or have the `[Handler]` attribute will be discovered
- **Use Case:** Explicit control over which classes are treated as handlers, avoiding accidental handler discovery

**`NotificationPublishStrategy`** (`NotificationPublishStrategy` enum)

- **Values:** `ForeachAwait`, `TaskWhenAll`, `FireAndForget`
- **Default:** `ForeachAwait`
- **Effect:** Controls how `PublishAsync` dispatches messages to multiple handlers
  - `ForeachAwait`: Invokes handlers sequentially, one at a time
  - `TaskWhenAll`: Invokes all handlers concurrently and waits for all to complete
  - `FireAndForget`: Fires all handlers in parallel without waiting

**`EnableGenerationCounter`** (`bool`)

- **Default:** `false`
- **Effect:** When `true`, includes a generation counter comment in generated files
- **Use Case:** Debugging source generator incremental caching

### Endpoint Properties

The following properties on `MediatorConfigurationAttribute` control endpoint generation:

**`EndpointDiscovery`** (`EndpointDiscovery` enum)

- **Values:** `None`, `Explicit`, `All`
- **Default:** `All`
- **Effect:** Controls which handlers generate API endpoints
  - `All`: All handlers with endpoint-compatible message types generate endpoints (default). Use `[HandlerEndpoint(Exclude = true)]` to opt out individual handlers.
  - `Explicit`: Only handlers with `[HandlerEndpoint]` or `[HandlerCategory]` attribute generate endpoints
  - `None`: No endpoints generated
- **See:** [Endpoints Guide](/guide/endpoints) for full documentation

**`EndpointRoutePrefix`** (`string?`)

- **Default:** `"api"`
- **Effect:** Sets a global route prefix that all category groups nest under. Categories auto-derive their route from their name (e.g., `[HandlerCategory("Products")]` → `products`), composing with the global prefix to produce `/api/products`.
- **Important:** Category-level `RoutePrefix` values without a leading `/` are **relative** to this global prefix. Don't include `api` in your category prefixes when using the default global prefix, or you'll get `/api/api/...`. Use a leading `/` on a category prefix to make it absolute (bypasses the global prefix).
- **To disable:** Set `EndpointRoutePrefix = ""` to remove the global prefix entirely, then use full paths in category prefixes.

**`AuthorizationRequired`** (`bool`)

- **Default:** `false`
- **Effect:** Sets the default authorization requirement for all handlers (both endpoints and direct mediator calls)
- **Use Case:** Secure-by-default API with opt-out for public handlers
- **Override:** Use `[HandlerAllowAnonymous]` on a handler class or method to opt out, or `[HandlerAuthorize]` to opt in specific handlers when this is `false`

**`EndpointFilters`** (`Type[]?`)

- **Default:** None
- **Effect:** Applies endpoint filters to the root MapGroup, affecting all generated endpoints
- **Example:** `EndpointFilters = new[] { typeof(LoggingFilter), typeof(ValidationFilter) }`

**`AuthorizationPolicies`** / **`AuthorizationRoles`**

- **Values:** String array / String array
- **Default:** None
- **Effect:** Sets default authorization policies and roles for all handlers globally

**`EndpointSummaryStyle`** (`EndpointSummaryStyle` enum)

- **Values:** `Exact`, `Spaced`
- **Default:** `Exact`
- **Effect:** Controls how endpoint summaries are generated from message type names
  - `Exact`: Uses the message type name as-is (e.g., `"GetProduct"`)
  - `Spaced`: Splits PascalCase into space-separated words (e.g., `"Get Product"`)

### Example Configuration

All configuration is done via the assembly attribute in any `.cs` file in your project:

```csharp
using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    HandlerLifetime = MediatorLifetime.Scoped,
    MiddlewareLifetime = MediatorLifetime.Scoped,
    EndpointDiscovery = EndpointDiscovery.All,
    EndpointRoutePrefix = "api"
)]
```

Your `.csproj` only needs the package reference and optional XML doc generation:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>

    <!-- Enable XML docs for endpoint summaries -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
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

## Mediator Configuration

### MediatorOptions Class

```csharp
public class MediatorOptions {
    public List<Assembly> Assemblies { get; set; } = [];
    public ServiceLifetime? MediatorLifetime { get; set; } // null = auto-detect
    public bool LogHandlers { get; set; }    // Log all discovered handlers at startup
    public bool LogMiddleware { get; set; }  // Log the middleware pipeline at startup
}
```

When `MediatorLifetime` is `null` (the default), the mediator is registered as **Scoped** in ASP.NET Core apps and **Singleton** otherwise. Set it explicitly to override auto-detection.

When `LogHandlers` is `true`, all registered handlers are printed in a formatted, aligned table during `AddMediator()`. When `LogMiddleware` is `true`, the middleware pipeline is printed in execution order:

```csharp
services.AddMediator(new MediatorOptions { LogHandlers = true, LogMiddleware = true });
// or
services.AddMediator(b => b.LogHandlers().LogMiddleware());
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

## Assembly-Level Configuration

Disable interceptors if you need to force DI dispatch:

```csharp
[assembly: MediatorConfiguration(DisableInterceptors = true)]
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
