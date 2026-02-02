# Troubleshooting

This guide covers common issues and debugging techniques when working with Foundatio Mediator.

## Viewing Generated Source Code

Since Foundatio Mediator uses source generators, it can be helpful to see the actual code being generated. This is useful for:

- Understanding how handlers are dispatched
- Debugging unexpected behavior
- Verifying interceptor generation
- Learning how the mediator works internally

### Enabling Generated File Output

Add the following to your `.csproj` file to emit generated files to disk:

```xml
<PropertyGroup>
    <!-- Output generated files to a folder in your project -->
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>

<ItemGroup>
    <!-- Exclude generated files from compilation (they're already compiled by the generator) -->
    <Compile Remove="$(CompilerGeneratedFilesOutputPath)/**/*.cs" />
    <!-- Include them as content so they show up in Solution Explorer -->
    <Content Include="$(CompilerGeneratedFilesOutputPath)/**/*.cs" />
</ItemGroup>
```

After building your project, you'll find the generated files in the `Generated` folder:

```
Generated/
  Foundatio.Mediator/
    Foundatio.Mediator.MediatorGenerator/
      YourHandler_YourMessage_Handler.g.cs
      YourProject_MediatorHandlers.g.cs
      InterceptsLocationAttribute.g.cs
      ...
```

### Understanding Generated Files

| File Pattern | Description |
|-------------|-------------|
| `*_Handler.g.cs` | Handler wrapper with strongly-typed dispatch and middleware pipeline |
| `*_MediatorHandlers.g.cs` | DI registration code for all handlers |
| `InterceptsLocationAttribute.g.cs` | Interceptor attribute for compile-time call redirection |
| `*_FoundatioModuleAttribute.g.cs` | Module marker for cross-assembly handler discovery |

### Viewing All Registered Handlers

The easiest way to see which handlers are registered at runtime is to use the `ShowRegisteredHandlers()` method:

```csharp
var mediator = serviceProvider.GetRequiredService<IMediator>();
((Mediator)mediator).ShowRegisteredHandlers();
```

This logs all registered handlers to your configured logger:

```
Registered Handlers:
- Message: MyApp.Messages.CreateOrder, Handler: OrderHandler_CreateOrder_Handler, IsAsync: True
- Message: MyApp.Messages.GetUser, Handler: UserHandler_GetUser_Handler, IsAsync: True
- Message: MyApp.Messages.UserCreated, Handler: NotificationHandler_UserCreated_Handler, IsAsync: False
```

**If a handler is missing from this list:**
- Verify the class name ends with `Handler` or `Consumer`
- Check that the method name is `Handle`, `HandleAsync`, `Consume`, or `ConsumeAsync`
- Ensure the handler isn't marked with `[FoundatioIgnore]`
- Handlers nested in generic classes are not supported (e.g., `OuterClass<T>.MyHandler`)
- Verify `AddHandlers()` was called during DI configuration

For deeper inspection, you can also [view the generated source files](#enabling-generated-file-output) to see the actual registration code in `*_MediatorHandlers.g.cs`.

### Example Generated Handler

Here's what a generated handler wrapper looks like:

```csharp
internal static class OrderHandler_CreateOrder_Handler
{
    public static async Task<Result<Order>> HandleAsync(
        IServiceProvider serviceProvider,
        CreateOrder message,
        CancellationToken cancellationToken)
    {
        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("OrderHandler");

        // OpenTelemetry activity
        using var activity = MediatorActivitySource.Instance.StartActivity("CreateOrder");

        // Middleware pipeline
        var validationMiddleware = GetMiddleware<ValidationMiddleware>(serviceProvider);
        validationMiddleware.Before(message);

        // Handler invocation
        var handlerInstance = GetOrCreateHandler(serviceProvider);
        var result = await handlerInstance.HandleAsync(message, cancellationToken);

        return result;
    }
}
```

## Common Issues

### Event Handlers Not Being Called

**Symptom:** Event handlers (notification handlers) are not being invoked when events are published.

**Cause:** Handler discovery is scoped to the current project and its referenced assemblies. This is by design for performance - the source generator only generates dispatch code for handlers it can see at compile time.

**Key Points:**
- Handlers are only discovered in the **current project** and **directly referenced projects**
- If Project A publishes an event, only handlers in Project A or projects that A references will be called
- Handlers in projects that reference Project A (downstream) will NOT be discovered

**Example:**
```
Common.Module (defines events + cross-cutting handlers)
    ↑
Orders.Module (references Common, publishes OrderCreated)
    ↑
Web (references Orders)
```

In this structure:
- When `Orders.Module` publishes `OrderCreated`, handlers in `Common.Module` ARE called (referenced)
- Handlers defined in `Web` are NOT called (Web references Orders, not the other way around)

**Solutions:**
1. **Move shared event handlers to a common/lower-level module** that all publishing modules reference
2. **Define events in the common module** so all handlers can subscribe to the same event type
3. Use `AddAssembly<T>()` in your mediator configuration to register handlers from specific assemblies at runtime

```csharp
// In Program.cs, register assemblies containing handlers
builder.Services.AddMediator(c =>
{
    c.AddAssembly<OrderCreated>();       // Common.Module (events + handlers)
    c.AddAssembly<GetDashboardReport>(); // Reports.Module
});
```

**Why this design?**
The source generator analyzes handlers at compile time to generate optimized, direct dispatch code. This eliminates runtime reflection and provides maximum performance. The trade-off is that handler discovery follows project reference boundaries.

### Handler Not Found

**Symptom:** `InvalidOperationException: No handler found for message type X`

**Causes:**
1. Handler class doesn't follow naming conventions (must end in `Handler` or `Consumer`)
2. Handler method doesn't follow naming conventions (`Handle`, `HandleAsync`, `Consume`, `ConsumeAsync`)
3. Handler is in a different assembly and not registered
4. Missing call to `AddHandlers()` in DI configuration
5. Handler is nested inside a generic class (not supported)

**Debugging:**
Use [`ShowRegisteredHandlers()`](#viewing-all-registered-handlers) to see which handlers are currently registered at runtime.

**Solutions:**
```csharp
// Ensure handlers are registered
services.AddMediator();
YourProject_MediatorHandlers.AddHandlers(services);

// Or use the [Handler] attribute for non-conventional names
[Handler]
public class MyCustomProcessor
{
    public void Process(MyMessage msg) { }
}
```

### Scoped Services Returning Same Instance

**Symptom:** Scoped services (like `DbContext`) return the same instance across different HTTP requests or DI scopes, causing stale data, disposed context errors, or cross-request data leakage.

**Cause:** The mediator is registered as singleton (default) and captures the root `IServiceProvider` at construction. All service resolution uses this root provider, making scoped services behave like singletons.

**Solution:** Register the mediator as scoped:

```csharp
services.AddMediator(b => b.SetMediatorLifetime(ServiceLifetime.Scoped));
```

This ensures each DI scope gets its own mediator that resolves services from the correct scope.

**See also:** [Mediator Lifetime and Scoped Services](./dependency-injection.md#mediator-lifetime-and-scoped-services)

### Multiple Handlers Found

**Symptom:** `InvalidOperationException: Multiple handlers found for message type X`

**Cause:** More than one handler exists for the same message type when using `Invoke`/`InvokeAsync`.

**Solution:** Use `PublishAsync` for messages that should be handled by multiple handlers, or remove duplicate handlers.

### Sync Handler with Async Call Site

**Symptom:** Compilation works but you want to understand the async wrapping.

When you call `InvokeAsync` on a synchronous handler, the generated code wraps the result:

```csharp
// Your sync handler
public string Handle(GetGreeting msg) => $"Hello, {msg.Name}!";

// Generated interceptor for InvokeAsync<string>
public static ValueTask<string> InterceptInvokeAsync(...)
{
    // Wraps sync result in ValueTask
    return new ValueTask<string>(Handle(...));
}
```

### Sync Invoke on Tuple-Returning Handler

**Symptom:** Compilation error `FMED010`

**Cause:** Handlers that return tuples (for cascading messages) cannot use synchronous `Invoke` because cascading messages must be published asynchronously.

**Solution:** Use `InvokeAsync` instead:
```csharp
// Error: Can't use sync Invoke with tuple return
var order = mediator.Invoke<Order>(new CreateOrder(...));

// Correct: Use InvokeAsync
var order = await mediator.InvokeAsync<Order>(new CreateOrder(...));
```

### Interceptors Not Working

**Symptom:** Calls go through DI lookup instead of direct dispatch.

**Causes:**
1. Interceptors disabled via `MediatorDisableInterceptors`
2. Cross-assembly calls (interceptors only work within the same assembly)
3. C# language version below 11

**Solutions:**
```xml
<PropertyGroup>
    <!-- Ensure interceptors are enabled -->
    <MediatorDisableInterceptors>false</MediatorDisableInterceptors>

    <!-- Ensure C# 11+ for interceptors -->
    <LangVersion>preview</LangVersion>

    <!-- Required for interceptors -->
    <InterceptorsNamespaces>$(InterceptorsNamespaces);Foundatio.Mediator</InterceptorsNamespaces>
</PropertyGroup>
```

### Middleware Not Executing

**Symptom:** Middleware `Before`/`After`/`Finally`/`ExecuteAsync` methods not being called.

**Causes:**
1. Middleware class doesn't end in `Middleware`
2. Middleware is in a different assembly than handlers
3. Method signatures don't match expected patterns

**Solution:** Ensure middleware follows conventions:
```csharp
// Class must end in "Middleware"
public class LoggingMiddleware
{
    // First parameter must be the message type (or object for all messages)
    public void Before(object message) { }
    public void After(object message) { }
    public void Finally(object message, Exception? ex) { }
}
```

## Diagnostic Codes

| Code | Severity | Description |
|------|----------|-------------|
| `FMED008` | Error | Synchronous invoke on asynchronous handler |
| `FMED009` | Error | Synchronous invoke on handler with async middleware |
| `FMED010` | Error | Synchronous invoke on handler with tuple return |

## Getting Help

If you encounter issues not covered here:

1. Check the [generated source code](#viewing-generated-source-code) to understand what's happening
2. Review the [GitHub repository](https://github.com/FoundatioFx/Foundatio.Mediator) for existing issues
3. Open a new issue with:
   - Your handler and message code
   - The generated wrapper code (from `Generated` folder)
   - The full error message or unexpected behavior
