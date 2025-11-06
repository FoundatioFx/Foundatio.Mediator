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

Use the `[FoundatioOrder]` attribute to control execution order:

```csharp
[FoundatioOrder(10)]
public class ValidationMiddleware
{
    // Runs early in Before, late in After/Finally
}

[FoundatioOrder(50)]
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

## Cross-Assembly Middleware Limitation

### Understanding the Limitation

Middleware must be defined in the **same project** as your message handlers. This is because middleware is discovered and woven into handler wrappers at compile-time by the source generator, which only has access to the current project's source code.

**This will NOT work:**

```text
Solution/
├── Common.Middleware/          # Project A
│   └── LoggingMiddleware.cs   # ❌ Won't be discovered
└── Orders.Handlers/            # Project B (references A)
    └── OrderHandler.cs         # Handler generated without logging
```

The source generator in `Orders.Handlers` cannot see the `LoggingMiddleware` source code from the referenced `Common.Middleware` project.

### Recommended Solution: Linked Files

The recommended approach is to use **linked files** to share middleware source code across multiple projects:

**Project Structure:**

```text
Solution/
├── Common.Middleware/
│   └── Middleware/
│       ├── LoggingMiddleware.cs
│       ├── ValidationMiddleware.cs
│       └── AuthorizationMiddleware.cs
├── Orders.Handlers/
│   ├── OrderHandler.cs
│   └── Middleware/              # Linked files from Common.Middleware
│       ├── LoggingMiddleware.cs   (link)
│       ├── ValidationMiddleware.cs (link)
│       └── AuthorizationMiddleware.cs (link)
└── Products.Handlers/
    ├── ProductHandler.cs
    └── Middleware/              # Same linked files
        └── ...
```

**Create linked files in your `.csproj`:**

```xml
<ItemGroup>
  <!-- Link middleware files from Common.Middleware project -->
  <Compile Include="..\Common.Middleware\Middleware\LoggingMiddleware.cs" Link="Middleware\LoggingMiddleware.cs" />
  <Compile Include="..\Common.Middleware\Middleware\ValidationMiddleware.cs" Link="Middleware\ValidationMiddleware.cs" />
  <Compile Include="..\Common.Middleware\Middleware\AuthorizationMiddleware.cs" Link="Middleware\AuthorizationMiddleware.cs" />
</ItemGroup>
```

**Use `internal` to avoid conflicts:**

Since the same middleware source file is compiled into multiple assemblies, declare middleware classes as `internal` to prevent type conflicts:

```csharp
// LoggingMiddleware.cs (in Common.Middleware)
namespace Common.Middleware;

// ✅ Use internal to avoid conflicts across assemblies
internal class LoggingMiddleware
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

    public void Finally(object message, Exception? ex)
    {
        if (ex != null)
            _logger.LogError(ex, "Failed handling {MessageType}", message.GetType().Name);
        else
            _logger.LogInformation("Completed {MessageType}", message.GetType().Name);
    }
}
```

### Alternative: Define Per-Project

If middleware is project-specific, define it directly in each handler project:

```csharp
// Orders.Handlers/Middleware/OrderValidationMiddleware.cs
namespace Orders.Handlers.Middleware;

internal class OrderValidationMiddleware
{
    public HandlerResult Before(IOrderCommand command)
    {
        if (!IsValid(command))
            return HandlerResult.ShortCircuit(Result.Invalid("Invalid order command"));

        return HandlerResult.Continue();
    }
}
```

### Why This Limitation Exists

The source generator analyzes your code at compile-time to create handler wrappers with middleware baked in for maximum performance. This compile-time approach:

- ✅ Eliminates runtime reflection
- ✅ Provides strongly-typed middleware parameters
- ✅ Enables interceptors for near-direct call performance
- ❌ Requires middleware source in the same compilation

Future versions may support cross-assembly middleware discovery via metadata, but for now, linked files provide a clean workaround.

### Example: ModularMonolith Sample

See the `samples/ModularMonolithSample/` directory for a complete example of middleware in a modular architecture.

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

- [Validation Middleware Example](../examples/validation-middleware) - Complete validation implementation
- [Handler Conventions](./handler-conventions) - Learn handler discovery rules
