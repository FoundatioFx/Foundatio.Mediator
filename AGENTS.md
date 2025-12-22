# AGENTS.md

This file provides essential context for AI coding agents working on Foundatio.Mediator - a convention-based C# mediator powered by source generators and interceptors.

## Project Overview

Foundatio.Mediator is a high-performance mediator library for .NET that achieves near-direct call performance through compile-time code generation. The project has two main components:

- **Runtime** (`src/Foundatio.Mediator.Abstractions/`) - Core abstractions, interfaces, and runtime support
- **Generators** (`src/Foundatio.Mediator/`) - Source generators and analyzers that emit handler wrappers and interceptor attributes

**Key innovation**: Zero runtime reflection. All handler dispatch is resolved at compile time via source generators that scan for handlers and emit strongly-typed wrappers with optional C# 11+ interceptor attributes for direct call redirection.

## Commands you can use

Build project (triggers source generators): `dotnet build`
Run tests (validate work): `dotnet test`
Run benchmarks: `cd benchmarks/Foundatio.Mediator.Benchmarks; dotnet run -c Release`
Run samples: `cd samples/ConsoleSample; dotnet run`
Clean (removes source generated files): `dotnet clean`

## Code Conventions

### Coding Standards

- Follow `.editorconfig` settings strictly
- Keep comments minimal - only for complex logic or non-obvious intent

### Source Generator Code Style

When writing code generation methods in `src/Foundatio.Mediator/`:

- **Prefer raw string literals** (`$$"""`) over manual `AppendLine()` chains for generating methods or code blocks
- Raw string literals make the generated code structure immediately visible and easier to review
- Use `{{variable}}` syntax within raw strings for interpolation
- Only use manual `AppendLine()` when conditional logic requires branching between lines

**Good** (raw string literal):

```csharp
source.AppendLine()
      .AppendLines($$"""
        [DebuggerStepThrough]
        private static {{handler.FullName}} GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (System.Threading.Volatile.Read(ref _isInDI) == 1)
            {
                return serviceProvider.GetRequiredService<{{handler.FullName}}>();
            }
        }
        """);
```

**Avoid** (manual AppendLine chains):

```csharp
source.AppendLine("[DebuggerStepThrough]");
source.AppendLine($"private static {handler.FullName} GetOrCreateHandler(IServiceProvider serviceProvider)");
source.AppendLine("{");
source.AppendLine($"    return serviceProvider.GetRequiredService<{handler.FullName}>();");
source.AppendLine("}");
```


### Handler Discovery Rules

Handlers are discovered at compile time by `HandlerAnalyzer.cs`. A class is treated as a handler if:

- Class name ends with `Handler` or `Consumer`, OR
- Class implements `IHandler` interface, OR
- Class has `[Handler]` attribute, OR
- Any method has `[Handler]` attribute

**Exclusions**:

- Generated classes in `Foundatio.Mediator` namespace ending in `_Handler`
- Classes marked with `[FoundatioIgnore]`

### Handler Method Conventions

Valid method names: `Handle`, `HandleAsync`, `Handles`, `HandlesAsync`, `Consume`, `ConsumeAsync`, `Consumes`, `ConsumesAsync`

Method signature rules:

- First parameter MUST be the message type
- Remaining parameters are resolved from DI (including `CancellationToken`)
- Can be static or instance methods
- Return types: `void`, `Task`, `ValueTask`, `TResponse`, `Task<TResponse>`, `ValueTask<TResponse>`, `Result<T>`, or tuple for cascading

Example:

```csharp
public class OrderHandler
{
    public async Task<Result<Order>> HandleAsync(GetOrder query, IOrderRepository repo, CancellationToken ct)
    {
        var order = await repo.FindAsync(query.Id, ct);
        return order ?? Result.NotFound($"Order {query.Id} not found");
    }

    // Tuple return - first element is response, rest are auto-published
    public async Task<(Order order, OrderCreated evt)> HandleAsync(CreateOrder cmd, IOrderRepository repo)
    {
        var order = await repo.CreateAsync(cmd);
        return (order, new OrderCreated(order.Id, order.CustomerId));
    }
}
```

### Middleware Conventions

Middleware classes must end with `Middleware`. Available methods:

- `Before(Async)` - Runs before handler (top-to-bottom order)
- `After(Async)` - Runs after successful handler (bottom-to-top order)
- `Finally(Async)` - Always runs (bottom-to-top order)

Example:

```csharp
[Middleware(Order = 1)] // Lower numbers run first in Before, last in After/Finally
public class LoggingMiddleware
{
    // Return value is type-matched into After/Finally parameters
    public Stopwatch Before(object message, HandlerExecutionInfo info)
    {
        return Stopwatch.StartNew();
    }

    public void Finally(object message, HandlerExecutionInfo info, Stopwatch sw, Exception? ex)
    {
        sw.Stop();
        // Log timing
    }
}
```

**Cross-Assembly Limitation**: Middleware must be defined in the same project as handlers. The source generator only has access to the current project's source code. Use linked files (`<Compile Include="..." Link="..." />` in `.csproj`) to share middleware across projects, and declare middleware classes as `internal` to avoid type conflicts.

### Execution Semantics

- **Invoke/InvokeAsync**: Requires EXACTLY one handler. Throws if zero or multiple handlers found.
- **PublishAsync**: Runs ALL applicable handlers (exact type, interfaces, base classes) in parallel.

## Testing Instructions

### Test Organization

All tests are in `tests/Foundatio.Mediator.Tests/`. DO NOT create ad-hoc console apps or sample projects for testing.

```bash
# Run all tests
dotnet test

# Run specific test file
dotnet test --filter "FullyQualifiedName~BasicHandlerGenerationTests"

# VS Code: Use "run tests" task (default test task)
```

### Generator Testing with Snapshots

Generator tests use the Verify library for snapshot verification:

1. Tests use `VerifyGenerated(source, new MediatorGenerator())` helper from `GeneratorTestBase.cs`
2. Snapshots stored as `*.verified.txt` files next to test files
3. After generator changes, carefully review snapshot diffs before accepting

Example test structure:

```csharp
public class MyGeneratorTests : GeneratorTestBase
{
    [Fact]
    public async Task TestName()
    {
        var source = """
            // C# code here
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}
```

### Integration Testing

- Prefer unit tests over integration tests
- When integration tests are needed, use existing message types from `Integration/` directory
- See `tests/Foundatio.Mediator.Tests/Integration/ScopedDependencyTests.cs` for examples

### Test Workflow

1. Make code changes
2. Run `dotnet test` immediately
3. Fix any failures before proceeding
4. Review snapshot diffs if generator tests fail
5. Accept snapshots only after verifying correctness

## Architecture Deep Dive

### Source Generator Pipeline

The `MediatorGenerator` orchestrates multiple analyzers and generators:

1. **HandlerAnalyzer** - Scans for handler classes and methods
2. **MiddlewareAnalyzer** - Finds middleware classes with Before/After/Finally methods
3. **CallSiteAnalyzer** - Locates all `mediator.InvokeAsync()`/`PublishAsync()` call sites
4. **HandlerGenerator** - Emits wrapper classes (e.g., `MessageType_Handler`) with static methods
5. **InterceptsLocationGenerator** - Emits `[InterceptsLocation]` attributes for C# 11+ (optional)
6. **DIRegistrationGenerator** - Emits `HandlerRegistration` entries for DI lookup

### Dispatch Mechanisms

**Same-assembly with C# 11+ (interceptors enabled)**:

- Calls to `mediator.InvokeAsync()` are intercepted at compile time
- Redirected to generated static wrapper methods
- Near-zero overhead (direct call performance)

**Cross-assembly or interceptors disabled**:

- Falls back to DI lookup via `HandlerRegistration` dictionary
- Keyed by `MessageTypeKey.Get(type)`
- Handler instances created via `ActivatorUtilities.CreateInstance()` if not registered in DI

Toggle interceptors: Add `<MediatorDisableInterceptors>true</MediatorDisableInterceptors>` to `.csproj`

### Handler Lifetime Management

Handlers are NOT auto-registered in DI by default. Options:

1. **Manual registration** (recommended):

   ```csharp
   services.AddTransient<OrderHandler>();
   services.AddScoped<UserHandler>();
   ```

2. **Auto-registration via MSBuild**:

   ```xml
   <MediatorHandlerLifetime>Transient|Scoped|Singleton</MediatorHandlerLifetime>
   ```

### Result Pattern

Use `Result<T>` for rich status handling without exceptions:

```csharp
// Success
return Result<Order>.Success(order);

// Created with location
return Result<Order>.Created(order, $"/orders/{order.Id}");

// Error cases
return Result.NotFound("Order not found");
return Result.Unauthorized("Access denied");
return Result.Conflict("Order already exists");
return Result.ValidationError(new ValidationError("Email", "Invalid format"));
```

Check results:

```csharp
if (result.IsSuccess)
    var order = result.Value;
else
    LogError(result.Status, result.Message, result.ValidationErrors);
```

### Middleware Short-Circuiting

Return `HandlerResult.ShortCircuit(value)` from Before middleware to skip handler execution:

```csharp
public HandlerResult Before(object message)
{
    if (!IsAuthorized(message))
        return HandlerResult.ShortCircuit(Result.Unauthorized());

    return HandlerResult.Continue();
}
```

## Configuration Options

### MSBuild Properties

Defined in `src/Foundatio.Mediator/Foundatio.Mediator.props`:

```xml
<!-- Disable interceptors (default: false) -->
<MediatorDisableInterceptors>true|false</MediatorDisableInterceptors>

<!-- Auto-register handlers in DI (default: None) -->
<MediatorHandlerLifetime>None|Transient|Scoped|Singleton</MediatorHandlerLifetime>

<!-- Disable OpenTelemetry tracing (default: false) -->
<MediatorDisableOpenTelemetry>true|false</MediatorDisableOpenTelemetry>
```

Requirements for interceptors:

- `LanguageVersion` must be `CSharp11` or higher
- `MediatorDisableInterceptors` must not be `true`

## File Structure Reference

```text
src/
  Foundatio.Mediator/              # Source generators
    MediatorGenerator.cs           # Main generator orchestrator
    HandlerAnalyzer.cs             # Discovers handler classes
    HandlerGenerator.cs            # Emits handler wrappers
    MiddlewareAnalyzer.cs          # Discovers middleware
    CallSiteAnalyzer.cs            # Finds mediator call sites
    InterceptsLocationGenerator.cs # Emits interceptor attributes
    DIRegistrationGenerator.cs     # Emits DI registrations

  Foundatio.Mediator.Abstractions/ # Runtime library
    IMediator.cs                   # Core mediator interface
    Mediator.cs                    # Default implementation
    HandlerRegistration.cs         # DI lookup metadata
    HandlerResult.cs               # Middleware flow control
    IResult.cs, Result.cs          # Result pattern types
    MediatorConfiguration.cs       # Runtime config
    HandlerContext.cs              # Execution context

tests/Foundatio.Mediator.Tests/
  GeneratorTestBase.cs             # Base class for generator tests
  BasicHandlerGenerationTests.cs  # Example generator tests
  Integration/                     # Integration test examples

samples/
  ConsoleSample/                   # Working example
    Handlers/Handlers.cs           # Handler examples
    Middleware/LoggingMiddleware.cs # Middleware example
    Program.cs, ServiceConfiguration.cs
```

## Common Patterns

### Cascading Messages (Tuple Returns)

First element = response; remaining non-null elements = auto-published events:

```csharp

```csharp
public async Task<(User user, UserCreated evt, WelcomeEmail email)> HandleAsync(RegisterUser cmd)
{
    var user = await CreateUser(cmd);
    return (
        user,                                    // Returned to caller
        new UserCreated(user.Id),                // Auto-published
        new WelcomeEmail(user.Email, user.Name)  // Auto-published
    );
}
```

### Generic Handlers

Support for open generics:

```csharp
public class EntityHandler<T> where T : IEntity
{
    public async Task<Result<T>> HandleAsync(GetEntity<T> query, IRepository<T> repo)
    {
        var entity = await repo.GetAsync(query.Id);
        return entity ?? Result.NotFound();
    }
}
```

## Security Considerations

- Always validate message parameters; never trust input
- Use middleware for cross-cutting security (authentication, authorization)
- Return `Result.Unauthorized()` or `Result.Forbidden()` instead of throwing
- Sanitize data before logging in middleware

## Additional Guidelines

- Reference existing instruction files for complete standards:
  - [.github/instructions/general.instructions.md](.github/instructions/general.instructions.md) - General coding guidelines
  - [.github/instructions/testing.instructions.md](.github/instructions/testing.instructions.md) - C# testing standards

- When modifying generator code, always:
  1. Update corresponding tests
  2. Run `dotnet test` to verify snapshots
  3. Check generated code in `samples/ConsoleSample/Generated/` for correctness

- For questions about usage patterns, reference:
  - `samples/ConsoleSample/` - Complete working examples
  - `docs/` - Detailed documentation
  - `tests/Foundatio.Mediator.Tests/Integration/` - Real-world scenarios
