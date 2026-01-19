# Handler Code Generation Strategy

This document outlines the complete code generation workflow for handlers in Foundatio.Mediator, including all optimization paths and decision points.

## Table of Contents

1. [Overview](#overview)
2. [Decision Properties](#decision-properties)
3. [Code Generation Flow](#code-generation-flow)
4. [Handler Instantiation Strategy](#handler-instantiation-strategy)
5. [Generated Method Structure](#generated-method-structure)
6. [Key Files](#key-files)

---

## Overview

The source generator produces one `.g.cs` file per handler containing a static wrapper class with multiple entry points:

```
{HandlerIdentifier}_{MessageTypeIdentifier}_Handler.g.cs
```

Each wrapper class contains:
- `HandleAsync` / `Handle` - Main entry point with full handler execution
- `UntypedHandleAsync` / `UntypedHandle` - Runtime dispatch entry point
- `HandleItem2Async`, `HandleItem3Async`, etc. - For tuple returns (cascade other items)
- `InterceptInvoke0`, `InterceptInvokeAsync0`, etc. - Interceptor methods for call sites
- `GetOrCreateHandler` - Handler instantiation logic (when needed)
- `_cachedHandler` - Static field for cached handler instances (when applicable)

---

## Decision Properties

All code generation decisions are driven by computed properties on `HandlerInfo`. These make the generation logic clear and testable.

### Dependency Requirements

| Property | Description |
|----------|-------------|
| `RequiresConstructorInjection` | Handler class has constructor parameters requiring DI |
| `RequiresMethodInjection` | Handler method has parameters beyond message and CancellationToken |
| `HasNoDependencies` | No constructor or method injection required, and all middleware has no dependencies |
| `IsStaticWithNoDependencies` | Static handler with no method injection, no middleware, no tuple return |

### Middleware Requirements

| Property | Description |
|----------|-------------|
| `HasMiddleware` | Handler has any middleware attached |
| `HasBeforeMiddleware` | Any middleware has a Before method |
| `HasAfterMiddleware` | Any middleware has an After method |
| `HasFinallyMiddleware` | Any middleware has a Finally method (requires try/catch) |
| `HasAsyncMiddleware` | Any middleware is async |
| `RequiresHandlerExecutionInfo` | Any middleware method needs HandlerExecutionInfo parameter |
| `RequiresMiddlewareInstances` | Any middleware requires instantiation (non-static) |

### Code Generation Requirements

| Property | Description |
|----------|-------------|
| `HasCascadingMessages` | Handler returns tuple with 2+ items (non-first items published) |
| `RequiresServiceProvider` | Handler or middleware needs DI resolution |
| `RequiresResultVariable` | Need to store handler result (for middleware, try/catch, or cascading) |
| `RequiresTryCatch` | Need try/catch/finally blocks (finally middleware exists) |
| `CanSkipAsyncStateMachine` | Can generate direct passthrough without async state machine |

### Handler Instantiation Strategy

| Property | Description |
|----------|-------------|
| `CanCacheHandlerInstance` | Handler can be singleton-cached (no dependencies or explicit Singleton lifetime) |
| `RequiresDIResolutionPerInvocation` | Handler must be resolved from DI every call (Scoped or Transient lifetime) |

---

## Code Generation Flow

### When `CanSkipAsyncStateMachine` is true (and no OpenTelemetry)

The handler can be called directly without generating an async state machine:

```csharp
public static ValueTask HandleAsync(IMediator mediator, MyMessage message, CancellationToken cancellationToken)
{
    // Static handler: call directly
    return MyStaticHandler.Handle(message, cancellationToken);

    // OR instance handler with no dependencies: use cached instance
    return _cachedHandler.HandleAsync(message, cancellationToken);
}
```

**Conditions for `CanSkipAsyncStateMachine`:**
```csharp
CanSkipAsyncStateMachine =>
    IsStaticWithNoDependencies ||  // Static, no method DI, no middleware, no tuple
    (HasNoDependencies && !HasMiddleware && !HasCascadingMessages);  // Instance, no deps, no middleware, no cascading
```

### When `RequiresTryCatch` is true (or OpenTelemetry enabled)

The generated code wraps execution in try/catch/finally:

```csharp
public static async ValueTask<TResult> HandleAsync(IMediator mediator, TMessage message, CancellationToken cancellationToken)
{
    var serviceProvider = (IServiceProvider)mediator;

    // OpenTelemetry setup (if enabled)
    using var activity = MediatorActivitySource.Instance.StartActivity("MessageType");

    // Handler execution info (if middleware needs it)
    var handlerExecutionInfo = new HandlerExecutionInfo(...);

    // Middleware instances (if non-static middleware)
    var loggingMiddleware = GetOrCreateLoggingMiddleware(serviceProvider);

    // Result variables
    TResult? result = default;
    Exception? exception = null;

    try
    {
        // Before middleware
        // Handler invocation
        // After middleware
    }
    catch (Exception ex)
    {
        exception = ex;
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
    finally
    {
        // Finally middleware (reverse order)
    }

    return result;
}
```

**Condition:**
```csharp
requiresTryCatch = handler.RequiresTryCatch || configuration.OpenTelemetryEnabled;

// Where RequiresTryCatch is:
RequiresTryCatch => HasFinallyMiddleware;
```

---

## Handler Instantiation Strategy

The `GenerateGetOrCreateHandler` method generates different code based on handler properties:

### 1. Scoped/Transient Lifetime (`RequiresDIResolutionPerInvocation`)

Always resolve from DI - no caching:

```csharp
private static MyHandler GetOrCreateHandler(IServiceProvider serviceProvider)
{
    return serviceProvider.GetRequiredService<MyHandler>();
}
```

### 2. Explicit Singleton Lifetime

Always resolve from DI - the user explicitly wants DI to manage the instance:

```csharp
private static MyHandler GetOrCreateHandler(IServiceProvider serviceProvider)
{
    return serviceProvider.GetRequiredService<MyHandler>();
}
```

**Note**: We don't cache Singleton handlers in static fields because:
- Static fields persist across different `IServiceProvider` instances (e.g., in tests)
- This would cause handlers with constructor dependencies to hold stale references
- Let the DI container handle singleton caching - it's optimized for this

### 3. No Constructor Dependencies, No Explicit Lifetime (`!RequiresConstructorInjection && Lifetime is None/Default`)

Generate static cached instance - no `GetOrCreateHandler` method needed:

```csharp
private static readonly MyHandler _cachedHandler = new();
```

**Note**: Method DI parameters don't prevent caching - only constructor dependencies matter for instantiation.

### 4. Has Constructor Dependencies, No Explicit Lifetime (None/Default)

Use `ActivatorUtilities.CreateInstance` and cache. **Important**: When lifetime is not explicitly set, we assume the handler is NOT registered in DI. Dependencies are still resolved from the service provider via `ActivatorUtilities`:

```csharp
private static MyHandler? _cachedHandler;

private static MyHandler GetOrCreateHandler(IServiceProvider serviceProvider)
{
    return _cachedHandler ??= ActivatorUtilities.CreateInstance<MyHandler>(serviceProvider);
}
```

---

## Generated Method Structure

### HandleAsync (Full Path)

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

    // 3. Handler execution info (if RequiresHandlerExecutionInfo)
    var handlerExecutionInfo = new HandlerExecutionInfo(...);

    // 4. Middleware instances (if RequiresMiddlewareInstances)
    var middleware = GetOrCreateMiddleware(serviceProvider);

    // 5. Before middleware result variables (if HasBeforeMiddleware with return)
    HandlerResult<T>? middlewareResult = null;

    // 6. Handler result variable (if RequiresResultVariable)
    TResponse? result = default;

    // 7. Exception variable (if RequiresTryCatch || OpenTelemetryEnabled)
    Exception? exception = null;

    try  // Only if requiresTryCatch
    {
        // 8. Before middleware calls (if HasBeforeMiddleware)
        middlewareResult = await BeforeMiddleware.BeforeAsync(message);
        if (middlewareResult.IsShortCircuited)
            return middlewareResult.Value;

        // 9. Handler invocation
        // - Static: FullName.Method(...)
        // - HasNoDependencies: _cachedHandler.Method(...)
        // - Otherwise: GetOrCreateHandler(serviceProvider).Method(...)
        result = await handlerInstance.HandleAsync(message, cancellationToken);

        // 10. After middleware calls (if HasAfterMiddleware, reverse order)
        await middleware.AfterAsync(message, result);
    }
    catch (Exception ex)  // Only if requiresTryCatch
    {
        exception = ex;
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
    finally  // Only if requiresTryCatch
    {
        // 11. Finally middleware (if HasFinallyMiddleware, reverse order)
        await middleware.FinallyAsync(message, exception);
    }

    // 12. Cascading (if HasCascadingMessages)
    // Publish tuple items 2+ to their handlers

    return result;
}
```

---

## Summary Tables

### When Each Feature is Generated

| Feature | Condition |
|---------|-----------|
| Async state machine | `!CanSkipAsyncStateMachine \|\| OpenTelemetryEnabled` |
| Service provider extraction | `RequiresServiceProvider` |
| Try/catch/finally | `RequiresTryCatch \|\| OpenTelemetryEnabled` |
| OpenTelemetry activity | `configuration.OpenTelemetryEnabled` |
| HandlerExecutionInfo | `RequiresHandlerExecutionInfo` |
| Middleware instances | `RequiresMiddlewareInstances` |
| Result variable | `RequiresResultVariable` |
| GetOrCreateHandler method | `!IsStatic && !HasNoDependencies` |
| Cached handler field | `!IsStatic && (HasNoDependencies \|\| Lifetime == "Singleton")` |

### Handler Accessor Decision

| Condition | Accessor |
|-----------|----------|
| `IsStatic` | `FullTypeName.Method(...)` |
| `HasNoDependencies` | `_cachedHandler.Method(...)` |
| Otherwise | `GetOrCreateHandler(serviceProvider).Method(...)` |

---

## Middleware Instantiation Strategy

Middleware instantiation follows the same pattern as handler instantiation. For each non-static middleware, the generator creates:
- A static cached field: `_cachedMiddlewareName`
- A `GetOrCreateMiddlewareName(IServiceProvider)` method (unless Scoped/Transient)

### 1. Scoped/Transient Lifetime (`RequiresDIResolutionPerInvocation`)

Always resolve from DI - no caching or generated method:

```csharp
// In EmitMiddlewareInstances
var loggingMiddleware = serviceProvider.GetRequiredService<LoggingMiddleware>();
```

### 2. Explicit Singleton Lifetime

Always resolve from DI - the user explicitly wants DI to manage the instance:

```csharp
// In EmitMiddlewareInstances
var loggingMiddleware = serviceProvider.GetRequiredService<LoggingMiddleware>();
```

**Note**: Same reasoning as handlers - we don't cache Singleton middleware in static fields.

### 3. No Constructor Dependencies, No Explicit Lifetime (`!RequiresConstructorInjection && Lifetime is None/Default`)

Simple cached instance using `new()`:

```csharp
private static SimpleMiddleware? _cachedSimpleMiddleware;

private static SimpleMiddleware GetOrCreateSimpleMiddleware(IServiceProvider serviceProvider)
{
    return _cachedSimpleMiddleware ??= new SimpleMiddleware();
}
```

**Note**: Method DI parameters don't prevent caching - only constructor dependencies matter for instantiation.

### 4. Has Constructor Dependencies, No Explicit Lifetime (None/Default)

Use `ActivatorUtilities.CreateInstance` and cache. **Important**: When lifetime is not explicitly set, we assume the middleware is NOT registered in DI. Dependencies are still resolved from the service provider via `ActivatorUtilities`:

```csharp
private static LoggingMiddleware? _cachedLoggingMiddleware;

private static LoggingMiddleware GetOrCreateLoggingMiddleware(IServiceProvider serviceProvider)
{
    return _cachedLoggingMiddleware ??= ActivatorUtilities.CreateInstance<LoggingMiddleware>(serviceProvider);
}
```

### MiddlewareInfo Decision Properties

| Property | Description |
|----------|-------------|
| `RequiresConstructorInjection` | Middleware class has constructor parameters |
| `RequiresMethodInjection` | Middleware methods need DI beyond message/HandlerExecutionInfo/exception/Before return value |
| `HasNoDependencies` | Static OR (no constructor injection AND no method injection) |
| `CanCacheInstance` | Has no dependencies OR explicit Singleton lifetime |
| `RequiresDIResolutionPerInvocation` | Scoped or Transient lifetime |

---

## Key Files

| File | Purpose |
|------|---------|
| `src/Foundatio.Mediator/Models/HandlerInfo.cs` | Handler metadata with all decision properties |
| `src/Foundatio.Mediator/Models/MiddlewareInfo.cs` | Middleware metadata |
| `src/Foundatio.Mediator/HandlerGenerator.cs` | Main handler code generation |
| `src/Foundatio.Mediator/Utility/InterceptorCodeEmitter.cs` | Interceptor code generation utilities |
| `src/Foundatio.Mediator/HandlerAnalyzer.cs` | Detects handlers in source |
| `src/Foundatio.Mediator/MiddlewareAnalyzer.cs` | Detects and matches middleware |
