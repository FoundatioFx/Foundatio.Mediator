# AI Instructions: Foundatio.Mediator Source Generator

**You are an expert AI assistant working on the Foundatio.Mediator source generator. This document defines the exact rules and patterns you must follow when modifying code generation logic.**

## Critical Principles

1. **Never break existing semantics** - Generated code must maintain backward compatibility
2. **Follow the patterns exactly** - Handler/middleware instantiation follows strict rules based on lifetime
3. **Performance is critical** - Avoid allocations, use aggressive inlining, cache where safe
4. **Test thoroughly** - All changes require running `dotnet build` then `dotnet test`
5. **Static caching rules**:
   - Only cache when handler/middleware has NO constructor dependencies AND lifetime is None/Default
   - OR when handler/middleware has constructor dependencies AND lifetime is None/Default (uses `ActivatorUtilities.CreateInstance`)
   - NEVER cache when lifetime is Scoped/Transient (always resolve from DI)
   - NEVER cache when lifetime is Singleton (always resolve from DI - let DI container manage singleton caching)

## Handler Instantiation Rules (CRITICAL)

The `GenerateGetOrCreateHandler` and `EmitHandlerInvocation` methods MUST follow these exact patterns:

### 1. Static Handlers

**No caching needed** - call directly:

```csharp
// In EmitHandlerInvocation:
string accessor = handler.FullName;
source.AppendLine($"{asyncModifier}{accessor}.{handler.MethodName}({parameters});");
```

### 2. Scoped/Transient Lifetime

**Always resolve from DI - NO caching, NO generated method**:

```csharp
// Check in EmitHandlerInvocation:
if (handler.RequiresDIResolutionPerInvocation)
{
    source.AppendLine($"var handlerInstance = serviceProvider.GetRequiredService<{handler.FullName}>();");
    accessor = "handlerInstance";
}
```

**Important**: `RequiresDIResolutionPerInvocation` returns true when:

- `handler.Lifetime` is `"Scoped"`, OR
- `handler.Lifetime` is `"Transient"`, OR
- MSBuild property `MediatorDefaultHandlerLifetime` is `"Scoped"` or `"Transient"` AND `handler.Lifetime` is `None`

### 3. Explicit Singleton Lifetime

**Always resolve from DI - NO static caching**:

```csharp
// Check in EmitHandlerInvocation:
if (string.Equals(handler.Lifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
{
    source.AppendLine($"var handlerInstance = serviceProvider.GetRequiredService<{handler.FullName}>();");
    accessor = "handlerInstance";
}
```

**Why no static caching for Singleton?**

- User explicitly wants DI to manage the instance
- Let the DI container handle singleton caching (it's optimized for this)
- Avoids potential issues with multiple ServiceProvider instances (e.g., in tests)

### 4. No Constructor Dependencies, Lifetime is None/Default

**Static cached instance with lazy initialization**:

```csharp
// In GenerateGetOrCreateHandler:
private static {handler.FullName}? _cachedHandler;

[DebuggerStepThrough]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static {handler.FullName} GetOrCreateHandler(IServiceProvider serviceProvider)
{
    return _cachedHandler ??= new {handler.FullName}();
}

// In EmitHandlerInvocation:
source.AppendLine("var handlerInstance = GetOrCreateHandler(serviceProvider);");
accessor = "handlerInstance";
```

### 5. Has Constructor Dependencies, Lifetime is None/Default

**Use `ActivatorUtilities.CreateInstance` and cache**:

```csharp
// In GenerateGetOrCreateHandler:
private static {handler.FullName}? _cachedHandler;

[DebuggerStepThrough]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static {handler.FullName} GetOrCreateHandler(IServiceProvider serviceProvider)
{
    return _cachedHandler ??= ActivatorUtilities.CreateInstance<{handler.FullName}>(serviceProvider);
}

// In EmitHandlerInvocation:
source.AppendLine("var handlerInstance = GetOrCreateHandler(serviceProvider);");
accessor = "handlerInstance";
```

**Important**: When lifetime is None (default), we assume the handler is NOT registered in DI. Dependencies are resolved from the service provider via `ActivatorUtilities`, but the handler instance itself is cached in a static field for performance.

## Middleware Instantiation Rules (CRITICAL)

The `GenerateMiddlewareInstantiation` and `EmitMiddlewareInstances` methods MUST follow these exact patterns:

### 1. Static Middleware

**No instantiation needed** - call directly:

```csharp
// In middleware invocation code:
string accessor = middleware.FullName;
```

### 2. Scoped/Transient Lifetime

**Always resolve from DI - NO caching, NO generated method**:

```csharp
// In EmitMiddlewareInstances:
if (m.RequiresDIResolutionPerInvocation)
{
    source.AppendLine($"var {varName} = serviceProvider.GetRequiredService<{m.FullName}>();");
}
```

### 3. Explicit Singleton Lifetime

**Always resolve from DI - NO static caching**:

```csharp
// In EmitMiddlewareInstances:
if (string.Equals(m.Lifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
{
    source.AppendLine($"var {varName} = serviceProvider.GetRequiredService<{m.FullName}>();");
}
```

### 4. No Constructor Dependencies, Lifetime is None/Default

**Static cached instance with lazy initialization**:

```csharp
// In GenerateMiddlewareInstantiation:
private static {m.FullName}? _cached{m.Identifier};

[DebuggerStepThrough]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static {m.FullName} GetOrCreate{m.Identifier}(IServiceProvider serviceProvider)
{
    return _cached{m.Identifier} ??= new {m.FullName}();
}

// In EmitMiddlewareInstances:
source.AppendLine($"var {varName} = GetOrCreate{m.Identifier}(serviceProvider);");
```

### 5. Has Constructor Dependencies, Lifetime is None/Default

**Use `ActivatorUtilities.CreateInstance` and cache**:

```csharp
// In GenerateMiddlewareInstantiation:
private static {m.FullName}? _cached{m.Identifier};

[DebuggerStepThrough]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static {m.FullName} GetOrCreate{m.Identifier}(IServiceProvider serviceProvider)
{
    return _cached{m.Identifier} ??= ActivatorUtilities.CreateInstance<{m.FullName}>(serviceProvider);
}

// In EmitMiddlewareInstances:
source.AppendLine($"var {varName} = GetOrCreate{m.Identifier}(serviceProvider);");
```

## Helper Methods Reference

### RequiresDIResolution(HandlerInfo, GeneratorConfiguration)

Returns `true` when handler MUST be resolved from DI every invocation (no caching):

```csharp
private static bool RequiresDIResolution(HandlerInfo handler, GeneratorConfiguration configuration)
{
    // Check explicit lifetime attribute first
    if (string.Equals(handler.Lifetime, "Scoped", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(handler.Lifetime, "Transient", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    // If no explicit lifetime set, check project default
    if (handler.Lifetime is "None" or null)
    {
        if (string.Equals(configuration.DefaultHandlerLifetime, "Scoped", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configuration.DefaultHandlerLifetime, "Transient", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}
```

### RequiresDIResolution(MiddlewareInfo, GeneratorConfiguration)

Same logic for middleware:

```csharp
private static bool RequiresDIResolution(MiddlewareInfo middleware, GeneratorConfiguration configuration)
{
    // Check explicit lifetime attribute first
    if (string.Equals(middleware.Lifetime, "Scoped", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(middleware.Lifetime, "Transient", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    // If no explicit lifetime set, check project default
    if (middleware.Lifetime is "None" or null)
    {
        if (string.Equals(configuration.DefaultMiddlewareLifetime, "Scoped", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configuration.DefaultMiddlewareLifetime, "Transient", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}
```

## Code Generation Decision Properties

All code generation decisions are driven by computed properties on `HandlerInfo` and `MiddlewareInfo`.

### Handler Properties

| Property                            | Description                                                                 |
|-------------------------------------|-----------------------------------------------------------------------------|
| `RequiresConstructorInjection`      | Handler class has constructor parameters requiring DI                       |
| `RequiresMethodInjection`           | Handler method has parameters beyond message and CancellationToken          |
| `HasNoDependencies`                 | No constructor or method injection, and all middleware has no dependencies  |
| `IsStaticWithNoDependencies`        | Static handler with no method injection, no middleware, no tuple return     |
| `HasMiddleware`                     | Handler has any middleware attached                                         |
| `HasBeforeMiddleware`               | Any middleware has a Before method                                          |
| `HasAfterMiddleware`                | Any middleware has an After method                                          |
| `HasFinallyMiddleware`              | Any middleware has a Finally method (requires try/catch)                    |
| `HasExecuteMiddleware`              | Any middleware has an ExecuteAsync method (wraps entire pipeline)           |
| `RequiresHandlerExecutionInfo`      | Any middleware method needs HandlerExecutionInfo parameter                  |
| `RequiresMiddlewareInstances`       | Any middleware requires instantiation (non-static)                          |
| `HasCascadingMessages`              | Handler returns tuple with 2+ items (non-first items published)             |
| `RequiresServiceProvider`           | Handler or middleware needs DI resolution                                   |
| `RequiresResultVariable`            | Need to store handler result (for middleware, try/catch, or cascading)      |
| `RequiresTryCatch`                  | Need try/catch/finally blocks (finally middleware exists)                   |
| `CanSkipAsyncStateMachine`          | Can generate direct passthrough without async state machine                 |
| `RequiresDIResolutionPerInvocation` | Handler must be resolved from DI every call (Scoped/Transient)              |

### Middleware Properties

| Property                            | Description                                                        |
|-------------------------------------|--------------------------------------------------------------------|
| `RequiresConstructorInjection`      | Middleware class has constructor parameters                        |
| `RequiresMethodInjection`           | Middleware methods need DI beyond standard parameters              |
| `HasNoDependencies`                 | Static OR (no constructor injection AND no method injection)       |
| `IsStatic`                          | Middleware class is static                                         |
| `RequiresDIResolutionPerInvocation` | Middleware must be resolved from DI every call (Scoped/Transient)  |

## Performance Optimization: CanSkipAsyncStateMachine

When `CanSkipAsyncStateMachine` is `true` AND OpenTelemetry is disabled, generate a direct passthrough method without async state machine overhead:

```csharp
public static ValueTask<TResponse> HandleAsync(
    IMediator mediator,
    TMessage message,
    CancellationToken cancellationToken)
{
    // Static handler: call directly
    return StaticHandler.HandleAsync(message, cancellationToken);

    // OR instance handler with no dependencies: use cached instance
    return _cachedHandler.HandleAsync(message, cancellationToken);
}
```

**Conditions:**

```csharp
CanSkipAsyncStateMachine =>
    IsStaticWithNoDependencies ||
    (HasNoDependencies && !HasMiddleware && !HasCascadingMessages);
```

## Generated Method Structure

### HandleAsync/Handle (Main Entry Point)

Full method structure with all possible features:

```csharp
public static async ValueTask<TResponse> HandleAsync(
    IMediator mediator,
    TMessage message,
    CancellationToken cancellationToken)
{
    // 1. Service provider (if RequiresServiceProvider)
    var serviceProvider = (IServiceProvider)mediator;

    // 2. OpenTelemetry (if configuration.OpenTelemetryEnabled)
    using var activity = MediatorActivitySource.Instance.StartActivity("...");
    activity?.SetTag("messaging.system", "Foundatio.Mediator");

    // 3. Handler execution info (if RequiresHandlerExecutionInfo)
    var handlerExecutionInfo = GetOrCreateHandlerExecutionInfo();

    // 4. Middleware instances (if RequiresMiddlewareInstances)
    var loggingMiddleware = GetOrCreateLoggingMiddleware(serviceProvider);
    // OR for Scoped/Transient/Singleton:
    var loggingMiddleware = serviceProvider.GetRequiredService<LoggingMiddleware>();

    // 5. Before middleware result variables (if HasBeforeMiddleware with return)
    LoggingMiddlewareResult? loggingMiddlewareResult = null;

    // 6. Handler result variable (if RequiresResultVariable)
    TResponse? result = default;

    // 7. Exception variable (if RequiresTryCatch || OpenTelemetryEnabled)
    Exception? exception = null;

    try  // Only if RequiresTryCatch || OpenTelemetryEnabled
    {
        // 8. Before middleware calls (if HasBeforeMiddleware)
        loggingMiddlewareResult = await loggingMiddleware.BeforeAsync(message);
        if (loggingMiddlewareResult.IsShortCircuited)
            return loggingMiddlewareResult.Value;

        // 9. Handler invocation
        // Static: FullName.Method(...)
        // Scoped/Transient/Singleton: serviceProvider.GetRequiredService<T>().Method(...)
        // Default/None: GetOrCreateHandler(serviceProvider).Method(...)
        var handlerInstance = GetOrCreateHandler(serviceProvider);
        result = await handlerInstance.HandleAsync(message, cancellationToken);

        // 10. After middleware calls (if HasAfterMiddleware, reverse order)
        await loggingMiddleware.AfterAsync(message, result);
    }
    catch (Exception ex)  // Only if RequiresTryCatch || OpenTelemetryEnabled
    {
        exception = ex;
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
    finally  // Only if RequiresTryCatch || OpenTelemetryEnabled
    {
        // 11. Finally middleware (if HasFinallyMiddleware, reverse order)
        await loggingMiddleware.FinallyAsync(message, exception);
    }

    // 12. Cascading (if HasCascadingMessages)
    // Publish tuple items 2+ to their handlers

    return result;
}
```

## Code Generation Style

### Prefer Raw String Literals

When generating code blocks, **always prefer raw string literals** (`$$"""`) over manual `AppendLine()` chains. This makes the generated code structure immediately visible and easier to review.

**Good** - structure is visible:

```csharp
source.AppendLine()
      .AppendLines($$"""
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static {{handler.FullName}} GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            return _cachedHandler ??= new {{handler.FullName}}();
        }
        """);
```

**Avoid** - hard to read and maintain:

```csharp
source.AppendLine("[DebuggerStepThrough]");
source.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
source.AppendLine($"private static {handler.FullName} GetOrCreateHandler(IServiceProvider serviceProvider)");
source.AppendLine("{");
source.AppendLine($"    return _cachedHandler ??= new {handler.FullName}();");
source.AppendLine("}");
```

### When to Use Manual AppendLine

Only use manual `AppendLine()` when:

- Conditional logic requires branching between lines
- Building dynamic line-by-line content
- Appending to existing content in a loop

```csharp
// Acceptable - conditional logic
if (handler.HasMiddleware)
{
    source.AppendLine("// Middleware pipeline");
    foreach (var m in handler.Middleware)
    {
        source.AppendLine($"var {m.Identifier} = GetOrCreate{m.Identifier}(serviceProvider);");
    }
}
```

### Variable Interpolation

Use `{{variable}}` syntax within raw strings for interpolation (note the double braces with `$$"""`):

```csharp
source.AppendLines($$"""
    private static {{handler.FullName}}? _cachedHandler;
    """);
```

## Key Files

| File                                                       | Purpose                                      |
|------------------------------------------------------------|----------------------------------------------|
| `src/Foundatio.Mediator/HandlerGenerator.cs`              | Main handler code generation orchestrator    |
| `src/Foundatio.Mediator/Models/HandlerInfo.cs`            | Handler metadata with decision properties    |
| `src/Foundatio.Mediator/Models/MiddlewareInfo.cs`         | Middleware metadata with decision properties |
| `src/Foundatio.Mediator/HandlerAnalyzer.cs`               | Discovers handlers in source code            |
| `src/Foundatio.Mediator/MiddlewareAnalyzer.cs`            | Discovers and matches middleware to handlers |
| `src/Foundatio.Mediator/Utility/InterceptorCodeEmitter.cs` | Interceptor code generation utilities       |

## Testing Requirements

**Before marking any work complete:**

1. Clean and rebuild: `dotnet clean ; dotnet build`
2. Run all tests: `dotnet test`
3. Verify all 195+ tests pass
4. Check for new compiler warnings
5. Review generated code for correctness (check `obj/Generated/` folders)

**Common test failures:**

- **Lifetime resolution bugs**: Check `RequiresDIResolution` helper is called correctly
- **Static caching pollution**: Ensure static fields are only used when safe (None/Default lifetime)
- **Middleware ordering**: After/Finally middleware must be reversed
- **Short-circuit logic**: Ensure `HandlerResult.IsShortCircuited` is checked correctly

## Common Mistakes to Avoid

1. **Never cache Singleton handlers in static fields** - Let DI manage singletons
2. **Never cache Scoped/Transient handlers** - Must resolve from DI every invocation
3. **Always call RequiresDIResolution helpers** - Don't check `Lifetime` property directly
4. **Match patterns exactly** - The instantiation logic must be consistent across all code paths
5. **Test with full suite** - Individual test success doesn't guarantee no test pollution
6. **Use raw string literals** - Prefer `$$"""` over manual `AppendLine()` chains for readability
7. **Preserve performance** - Avoid allocations, use `AggressiveInlining`, cache where safe
