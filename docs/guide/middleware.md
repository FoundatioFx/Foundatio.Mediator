# Middleware

Middleware in Foundatio Mediator provides a powerful pipeline for implementing cross-cutting concerns like validation, logging, authorization, and error handling. Middleware can run before, after, and finally around handler execution.

## Basic Middleware

Create middleware by following naming conventions:

```csharp
public class LoggingMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public void Before(object message)
    {
        _logger.LogInformation("Handling {MessageType}", message.GetType().Name);
    }

    public void After(object message)
    {
        _logger.LogInformation("Handled {MessageType}", message.GetType().Name);
    }
}
```

## Middleware Conventions

### Class Names

Middleware classes must end with `Middleware`

### Method Names

Valid middleware method names:

- `Before` / `BeforeAsync`
- `After` / `AfterAsync`
- `Finally` / `FinallyAsync`

### Method Parameters

- **First parameter**: The message (can be `object`, interface, or concrete type)
- **Remaining parameters**: Injected via DI (including `CancellationToken`)

## Lifecycle Methods

### Before

Runs before the handler. Can return values that are passed to `After` and `Finally`:

```csharp
public class TimingMiddleware
{
    public Stopwatch Before(object message)
    {
        return Stopwatch.StartNew();
    }

    public void Finally(object message, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        Console.WriteLine($"Handled {message.GetType().Name} in {stopwatch.ElapsedMilliseconds}ms");
    }
}
```

### After

Runs after successful handler completion. Only called if the handler succeeds:

```csharp
public class AuditMiddleware
{
    public void After(object message, IUserContext userContext)
    {
        _auditLog.Record($"User {userContext.UserId} executed {message.GetType().Name}");
    }
}
```

### Finally

Always runs, regardless of success or failure. Receives exception if handler failed:

```csharp
public class ErrorHandlingMiddleware
{
    public void Finally(object message, Exception? exception)
    {
        if (exception != null)
        {
            _errorLog.Record(message, exception);
        }
    }
}
```

## Short-Circuiting with HandlerResult

Middleware can short-circuit handler execution by returning a `HandlerResult` from the `Before` method:

### Real-World Validation Example

Let's look at the validation middleware from the sample:

```csharp
public class ValidationMiddleware
{
    public HandlerResult Before(object message)
    {
        if (!IsValid(message))
            return Result.Invalid("Validation failed"); // Short-circuit

        return HandlerResult.Continue();
    }
}
```

### Short-Circuit Usage

```csharp
public class AuthorizationMiddleware
{
    public HandlerResult Before(object message, IUserContext userContext)
    {
        if (!IsAuthorized(userContext, message))
        {
            // Short-circuit with forbidden result
            return HandlerResult.ShortCircuit(Result.Forbidden("Access denied"));
        }

        // Continue to handler
        return HandlerResult.Continue();
    }

    private bool IsAuthorized(IUserContext user, object message)
    {
        // Authorization logic
        return true;
    }
}
```

## State Passing Between Lifecycle Methods

Values returned from `Before` are automatically injected into `After` and `Finally` by type:

```csharp
public class TransactionMiddleware
{
    public IDbTransaction Before(object message, IDbContext context)
    {
        return context.BeginTransaction();
    }

    public async Task After(object message, IDbTransaction transaction)
    {
        await transaction.CommitAsync();
    }

    public async Task Finally(object message, IDbTransaction transaction, Exception? ex)
    {
        if (ex != null)
            await transaction.RollbackAsync();

        await transaction.DisposeAsync();
    }
}
```

### Tuple State Returns

You can return multiple values using tuples:

```csharp
public class ComplexMiddleware
{
    public (Stopwatch timer, string correlationId) Before(object message)
    {
        return (Stopwatch.StartNew(), Guid.NewGuid().ToString());
    }

    public void Finally(object message, Stopwatch timer, string correlationId, Exception? ex)
    {
        timer.Stop();
        _logger.LogInformation("Correlation {CorrelationId} completed in {Ms}ms",
            correlationId, timer.ElapsedMilliseconds);
    }
}
```

## Middleware Ordering

Use the `[Middleware]` attribute to control execution order:

```csharp
[Middleware(10)]
public class ValidationMiddleware
{
    // Runs early in Before, late in After/Finally
}

[Middleware(50)]
public class LoggingMiddleware
{
    // Runs later in Before, earlier in After/Finally
}
```

**Default ordering** (without explicit order):

1. Message-specific middleware
2. Interface-based middleware
3. Object-based middleware

**Execution flow**:

- `Before`: Lower order values run first
- `After`/`Finally`: Higher order values run first (reverse order for proper nesting)

## Message-Specific Middleware

Target specific message types or interfaces:

```csharp
// Only runs for ICommand messages
public class CommandMiddleware
{
    public void Before(ICommand command)
    {
        _commandLogger.Log($"Executing command: {command.GetType().Name}");
    }
}

// Only runs for CreateOrder messages
public class OrderCreationMiddleware
{
    public HandlerResult Before(CreateOrder command)
    {
        if (_orderService.IsDuplicate(command))
            return HandlerResult.ShortCircuit(Result.Conflict("Duplicate order"));

        return HandlerResult.Continue();
    }
}
```

## Real-World Examples

### Comprehensive Logging Middleware

Here's the logging middleware from the sample project:

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

### Caching Middleware

```csharp
public class CachingMiddleware
{
    private readonly IMemoryCache _cache;

    public CachingMiddleware(IMemoryCache cache) => _cache = cache;

    public HandlerResult Before(IQuery query)
    {
        var cacheKey = $"{query.GetType().Name}:{GetQueryKey(query)}";

        if (_cache.TryGetValue(cacheKey, out var cachedResult))
        {
            return HandlerResult.ShortCircuit(cachedResult);
        }

        return HandlerResult.Continue();
    }

    public void After(IQuery query, object result)
    {
        var cacheKey = $"{query.GetType().Name}:{GetQueryKey(query)}";
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
    }
}
```

## Async Middleware

All lifecycle methods support async versions:

```csharp
public class AsyncMiddleware
{
    public async Task<Stopwatch> BeforeAsync(object message, CancellationToken ct)
    {
        await SomeAsyncSetup(ct);
        return Stopwatch.StartNew();
    }

    public async Task AfterAsync(object message, Stopwatch sw, CancellationToken ct)
    {
        await SomeAsyncCleanup(ct);
    }

    public async Task FinallyAsync(object message, Stopwatch sw, Exception? ex, CancellationToken ct)
    {
        sw.Stop();
        await LogTiming(message, sw.ElapsedMilliseconds, ex, ct);
    }
}
```

## Middleware Registration

Middleware are discovered automatically, but you can control their lifetime by registering them in DI:

```csharp
// Singleton (default behavior)
services.AddSingleton<ValidationMiddleware>();

// Scoped (new instance per request)
services.AddScoped<DatabaseTransactionMiddleware>();

// Transient (new instance per use)
services.AddTransient<DisposableMiddleware>();
```

## Middleware Discovery

Middleware is automatically discovered by the Foundatio.Mediator source generator. To share middleware across projects:

1. **Create a middleware project** with Foundatio.Mediator package referenced
2. **Reference that project** from your handler projects
3. **Ensure the handler project** also references Foundatio.Mediator

The source generator will discover middleware in referenced assemblies that have the Foundatio.Mediator source generator.

### Discovery Rules

Middleware classes are found using:

1. **Naming Convention**: Classes ending with `Middleware` (e.g., `LoggingMiddleware`, `ValidationMiddleware`)
2. **Attribute**: Classes marked with `[Middleware]` attribute

### Example: Cross-Assembly Middleware

```text
Solution/
├── Common.Middleware/              # Shared middleware project
│   ├── Common.Middleware.csproj    # References Foundatio.Mediator
│   └── LoggingMiddleware.cs        # Discovered by convention
└── Orders.Handlers/                # Handler project
    ├── Orders.Handlers.csproj      # References Common.Middleware AND Foundatio.Mediator
    └── OrderHandler.cs             # Uses LoggingMiddleware automatically
```

**Common.Middleware.csproj:**

```xml
<ItemGroup>
  <PackageReference Include="Foundatio.Mediator" />
</ItemGroup>
```

**Orders.Handlers.csproj:**

```xml
<ItemGroup>
  <ProjectReference Include="..\Common.Middleware\Common.Middleware.csproj" />
  <PackageReference Include="Foundatio.Mediator" />
</ItemGroup>
```

**Common.Middleware/LoggingMiddleware.cs:**

```csharp
namespace Common.Middleware;

// Discovered by naming convention (ends with Middleware)
public class LoggingMiddleware
{
    public void Before(object message, ILogger logger)
    {
        logger.LogInformation("Handling {MessageType}", message.GetType().Name);
    }
}
```

The middleware will automatically be applied to all handlers in `Orders.Handlers` project.

> **💡 Complete Example**: See the [Modular Monolith Sample](https://github.com/FoundatioFx/Foundatio.Mediator/tree/main/samples/ModularMonolithSample) for a working demonstration of cross-assembly middleware in a multi-module application with shared middleware in `Common.Module` being used by `Products.Module` and `Orders.Module`.

### Setting Middleware Order

Control execution order using the `[Middleware(Order = n)]` attribute:

```csharp
[Middleware(Order = 1)]  // Runs first in Before, last in After/Finally
public class LoggingMiddleware { }

[Middleware(Order = 10)] // Runs later in Before, earlier in After/Finally
public class PerformanceMiddleware { }
```

**Execution flow:**

- `Before`: Lower order values run first
- `After`/`Finally`: Higher order values run first (reverse order for proper nesting)

## Ignoring Middleware

Use `[FoundatioIgnore]` to exclude middleware classes or methods:

```csharp
[FoundatioIgnore] // Entire class ignored
public class DisabledMiddleware
{
    public void Before(object message) { }
}

public class PartialMiddleware
{
    public void Before(object message) { }

    [FoundatioIgnore] // Only this method ignored
    public void After(object message) { }
}
```

## Best Practices

### 1. Keep Middleware Focused

Each middleware should handle one concern:

```csharp
// ✅ Good - single responsibility
public class ValidationMiddleware { }
public class LoggingMiddleware { }
public class AuthorizationMiddleware { }

// ❌ Avoid - multiple responsibilities
public class EverythingMiddleware { }
```

### 2. Use Appropriate Lifecycle Methods

```csharp
// ✅ Validation in Before (can short-circuit)
public HandlerResult Before(object message) => ValidateMessage(message);

// ✅ Logging in Finally (always runs)
public void Finally(object message, Exception? ex) => LogResult(message, ex);

// ❌ Don't validate in After (handler already ran)
```

### 3. Handle Exceptions Gracefully

```csharp
public void Finally(object message, Exception? exception)
{
    if (exception != null)
    {
        // Log, notify, cleanup, etc.
        _logger.LogError(exception, "Handler failed for {MessageType}", message.GetType().Name);
    }
}
```

### 4. Use Strongly-Typed Message Parameters

```csharp
// ✅ Specific to commands
public void Before(ICommand command) { }

// ✅ Specific to queries
public void Before(IQuery query) { }

// ⚠️ Generic (runs for everything)
public void Before(object message) { }
```

## Next Steps

- [Modular Monolith Sample](../../samples/ModularMonolithSample/) - Complete working example of cross-assembly middleware
- [Handler Conventions](./handler-conventions) - Learn handler discovery rules
