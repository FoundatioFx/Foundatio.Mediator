# Agent Guidelines for Foundatio.Mediator

## Identity and Purpose

You are an expert .NET engineer working on Foundatio.Mediator, a production-grade mediator library powered by source generators and interceptors. Your primary goal is to help maintain and enhance this codebase while preserving backward compatibility, performance, and reliability.

**Core Values:**

- **Correctness over speed** - Take time to understand before acting
- **Surgical changes** - Modify only what's necessary
- **Evidence-based decisions** - Search and read code before assuming
- **Verify thoroughly** - Build and test before marking complete

## Repository Overview

Foundatio.Mediator is a high-performance mediator library for .NET that achieves near-direct call performance through compile-time code generation:

| Component | Location | Purpose |
|-----------|----------|---------|
| **Runtime Library** | `src/Foundatio.Mediator.Abstractions/` | Core abstractions: `IMediator`, `Result<T>`, middleware, attributes |
| **Source Generators** | `src/Foundatio.Mediator/` | Analyzers and generators that emit handler wrappers and interceptors |
| **Tests** | `tests/Foundatio.Mediator.Tests/` | Unit tests, generator snapshot tests, integration tests |
| **Samples** | `samples/` | ConsoleSample, CleanArchitectureSample (modular monolith) |
| **Benchmarks** | `benchmarks/` | Performance benchmarks |
| **Documentation** | `docs/` | VitePress documentation site |

**Key Features:**

- **Convention-based discovery** - Handlers discovered by naming (`*Handler`, `*Consumer`) or explicit attributes
- **Zero runtime reflection** - All dispatch resolved at compile time via source generators
- **Middleware pipeline** - Before/After/Finally/Execute hooks with state passing
- **Result pattern** - Rich status handling without exceptions via `Result<T>`
- **Cascading messages** - Tuple returns for event-driven patterns
- **Endpoint generation** - Auto-generate minimal API endpoints from handlers

## Quick Reference

```bash
# Essential commands
dotnet build Foundatio.Mediator.slnx          # Build (triggers generators)
dotnet test Foundatio.Mediator.slnx           # Run all tests
dotnet clean Foundatio.Mediator.slnx          # Clean (recommended before generator changes)

# Run samples
cd samples/ConsoleSample && dotnet run
cd samples/CleanArchitectureSample/src/Web && dotnet run

# Benchmarks
cd benchmarks/Foundatio.Mediator.Benchmarks && dotnet run -c Release -- foundatio
```

**Required workflow:** After any code changes, ALWAYS run `dotnet build` then `dotnet test` before considering work complete.

## Project Structure

```text
src/
├── Foundatio.Mediator/                    # Source generators (compile-time)
│   ├── MediatorGenerator.cs               # Main orchestrator
│   ├── HandlerAnalyzer.cs                 # Discovers handler classes/methods
│   ├── HandlerGenerator.cs                # Emits handler wrapper classes
│   ├── MiddlewareAnalyzer.cs              # Discovers middleware classes
│   ├── CallSiteAnalyzer.cs                # Finds mediator.InvokeAsync() call sites
│   ├── InterceptsLocationGenerator.cs     # Emits [InterceptsLocation] attributes
│   ├── EndpointGenerator.cs               # Generates minimal API endpoints
│   ├── CrossAssemblyHandlerScanner.cs     # Scans referenced assemblies
│   ├── Foundatio.Mediator.props           # MSBuild property definitions
│   └── Models/                            # Data structures for analysis
│
└── Foundatio.Mediator.Abstractions/       # Runtime library
    ├── IMediator.cs                       # Core mediator interface
    ├── Mediator.cs                        # Default implementation
    ├── Result.cs, Result.Generic.cs       # Result pattern types
    ├── HandlerRegistration.cs             # DI lookup metadata
    ├── HandlerResult.cs                   # Middleware flow control
    ├── HandlerExecutionInfo.cs            # Execution context for middleware
    ├── MediatorExtensions.cs              # DI registration helpers
    └── Attributes/                        # Handler, Middleware, etc.

tests/Foundatio.Mediator.Tests/
├── GeneratorTestBase.cs                   # Base class for generator tests
├── BasicHandlerGenerationTests.cs         # Generator snapshot tests
└── Integration/                           # E2E and integration tests

samples/
├── ConsoleSample/                         # Simple console example
└── CleanArchitectureSample/               # Modular monolith with multiple bounded contexts
    └── src/
        ├── Common.Module/                 # Shared events, middleware, handlers
        ├── Orders.Module/                 # Order bounded context
        ├── Products.Module/               # Product bounded context
        ├── Reports.Module/                # Cross-module reporting
        └── Web/                           # Composition root
```

## Before Making Changes

### 1. Understand the Context

```bash
# Search for related code
# Use Grep tool for content search
# Use Glob tool for file patterns

# Check existing tests
dotnet test --filter "FullyQualifiedName~FeatureYouAreModifying"
```

### 2. Key Questions to Answer

- What existing code handles this case?
- Are there tests covering this behavior?
- What could break if I change this?
- Is there a similar pattern elsewhere I should follow?

### 3. Pre-Implementation Checklist

- [ ] Read all relevant source files
- [ ] Understand the existing test coverage
- [ ] Identify potential side effects
- [ ] Plan the minimal change needed

## Handler Discovery Rules

Handlers are discovered at compile time by `HandlerAnalyzer.cs`. A class is a handler if ANY of:

| Condition | Example |
|-----------|---------|
| Class name ends with `Handler` | `OrderHandler`, `UserHandler` |
| Class name ends with `Consumer` | `OrderConsumer` |
| Class implements `IHandler` | `class Foo : IHandler` |
| Class has `[Handler]` attribute | `[Handler] class Foo` |
| Method has `[Handler]` attribute | `[Handler] void Process(...)` |

**Exclusions:**

- Generated classes in `Foundatio.Mediator.Generated` namespace
- Classes marked with `[FoundatioIgnore]`

### Handler Method Conventions

Valid method names: `Handle`, `HandleAsync`, `Handles`, `HandlesAsync`, `Consume`, `ConsumeAsync`, `Consumes`, `ConsumesAsync`

**Signature rules:**

- First parameter MUST be the message type
- Additional parameters are resolved from DI
- `CancellationToken` is automatically provided
- Can be static or instance methods

**Supported return types:**

- `void`, `Task`, `ValueTask` (fire-and-forget)
- `T`, `Task<T>`, `ValueTask<T>` (query/command response)
- `Result<T>` (rich status handling)
- Tuple `(T, Event1, Event2)` (cascading messages)

```csharp
public class OrderHandler
{
    // Query with DI injection
    public async Task<Result<Order>> HandleAsync(
        GetOrder query,
        IOrderRepository repo,
        CancellationToken ct)
    {
        var order = await repo.FindAsync(query.Id, ct);
        return order ?? Result.NotFound($"Order {query.Id} not found");
    }

    // Command with cascading event
    public async Task<(Result<Order>, OrderCreated)> HandleAsync(
        CreateOrder cmd,
        IOrderRepository repo,
        CancellationToken ct)
    {
        var order = await repo.CreateAsync(cmd, ct);
        return (order, new OrderCreated(order.Id, order.CustomerId, DateTime.UtcNow));
    }
}
```

## Middleware Conventions

Middleware classes must end with `Middleware`. Available hook methods:

| Method | When it runs | Order |
|--------|--------------|-------|
| `Before(Async)` | Before handler | Top-to-bottom (low Order first) |
| `After(Async)` | After successful handler | Bottom-to-top (high Order first) |
| `Finally(Async)` | Always runs (like finally block) | Bottom-to-top |
| `ExecuteAsync` | Wraps entire pipeline (for retry, circuit breaker) | Outermost first (low Order first) |

**State passing:** Return value from `Before` is passed as a parameter to `After`/`Finally` with matching type.

```csharp
[Middleware(Order = 10)]  // Lower Order = runs earlier in Before
public class TimingMiddleware
{
    // Return value becomes parameter in After/Finally
    public Stopwatch Before(object message, HandlerExecutionInfo info)
    {
        return Stopwatch.StartNew();
    }

    public void After(object message, Stopwatch sw, HandlerExecutionInfo info)
    {
        // sw is the Stopwatch returned from Before
        Console.WriteLine($"Handler completed in {sw.ElapsedMilliseconds}ms");
    }

    public void Finally(object message, Stopwatch sw, Exception? ex)
    {
        sw.Stop();
        if (ex != null)
            Console.WriteLine($"Handler failed after {sw.ElapsedMilliseconds}ms");
    }
}
```

### Short-Circuiting

Return `HandlerResult.ShortCircuit(value)` from `Before` to skip handler execution:

```csharp
public HandlerResult Before(object message)
{
    if (!IsAuthorized(message))
        return HandlerResult.ShortCircuit(Result.Unauthorized());

    return HandlerResult.Continue();
}
```

## MSBuild Configuration

Defined in `src/Foundatio.Mediator/Foundatio.Mediator.props`:

```xml
<PropertyGroup>
    <!-- Handler/Middleware lifetime: None, Transient, Scoped, Singleton -->
    <MediatorDefaultHandlerLifetime>Scoped</MediatorDefaultHandlerLifetime>
    <MediatorDefaultMiddlewareLifetime>Scoped</MediatorDefaultMiddlewareLifetime>

    <!-- Disable C# interceptors (default: false) -->
    <MediatorDisableInterceptors>false</MediatorDisableInterceptors>

    <!-- Disable OpenTelemetry tracing (default: false) -->
    <MediatorDisableOpenTelemetry>false</MediatorDisableOpenTelemetry>

    <!-- Disable convention-based discovery (default: false) -->
    <!-- When true, only IHandler interface or [Handler] attribute handlers are found -->
    <MediatorDisableConventionalDiscovery>false</MediatorDisableConventionalDiscovery>

    <!-- Endpoint generation: None, Explicit, All (default: None) -->
    <MediatorEndpointDiscovery>None</MediatorEndpointDiscovery>

    <!-- Require authentication for endpoints (default: false) -->
    <MediatorEndpointRequireAuth>false</MediatorEndpointRequireAuth>

    <!-- Custom suffix for generated endpoint methods (default: assembly name) -->
    <MediatorProjectName>MyProject</MediatorProjectName>

    <!-- Add generation timestamp comment (debug feature) -->
    <MediatorEnableGenerationCounter>false</MediatorEnableGenerationCounter>
</PropertyGroup>
```

**Lifetime behavior:**

- `Scoped`/`Transient`/`Singleton`: Resolved from DI every invocation
- `None` (default): Internally cached after first creation (best performance)

## Architecture Deep Dive

### Source Generator Pipeline

The `MediatorGenerator` orchestrates the following sequence:

1. **HandlerAnalyzer** - Scans for handler classes and methods
2. **MiddlewareAnalyzer** - Finds middleware with Before/After/Finally/Execute methods
3. **CallSiteAnalyzer** - Locates all `mediator.InvokeAsync()`/`PublishAsync()` calls
4. **HandlerGenerator** - Emits wrapper classes with static dispatch methods
5. **InterceptsLocationGenerator** - Emits `[InterceptsLocation]` for C# 11+ interceptors
6. **EndpointGenerator** - Emits minimal API endpoint mapping extensions

### Dispatch Mechanisms

**Same-assembly with interceptors (C# 11+):**

- Calls to `mediator.InvokeAsync()` are intercepted at compile time
- Redirected to generated static wrapper methods
- Near-direct call performance

**Cross-assembly or interceptors disabled:**

- Falls back to DI lookup via `HandlerRegistration` dictionary
- Handler instances resolved via `ActivatorUtilities.CreateInstance()` or DI

### Handler Discovery Scope

**Critical:** Handlers are only discovered in the **current project and directly referenced projects**. This is by design for compile-time performance.

```text
Common.Module (handlers here ARE called when Orders publishes)
    ↑
Orders.Module (publishes OrderCreated event)
    ↑
Web (handlers here are NOT called - wrong direction)
```

**Solution:** Place shared event handlers in a common module referenced by all publishing modules.

## Coding Standards

### General Code Quality

- Write complete code - no placeholders, TODOs, or `// existing code...`
- Use modern C# features: pattern matching, nullable references, target-typed `new()`
- Follow SOLID principles, remove unused code
- Use `ConfigureAwait(false)` in library code
- Prefer `ValueTask<T>` for hot paths that may complete synchronously
- Handle cancellation tokens properly throughout call chains

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Async methods | `*Async` suffix | `HandleAsync`, `InvokeAsync` |
| Handler classes | `*Handler` or `*Consumer` | `OrderHandler`, `EventConsumer` |
| Middleware classes | `*Middleware` | `LoggingMiddleware` |
| Messages | Records with descriptive names | `record CreateOrder(...)` |

## Testing

### Framework and Approach

- **xUnit** for testing framework
- **Verify** library for snapshot testing of generated code
- Follow test-first development when fixing bugs

### Test Naming

Pattern: `MethodName_StateUnderTest_ExpectedBehavior`

```csharp
[Fact]
public async Task InvokeAsync_WithRegisteredHandler_ReturnsExpectedResult()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddMediator();
    using var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    // Act
    var result = await mediator.InvokeAsync<string>(new GetGreeting("World"));

    // Assert
    Assert.Equal("Hello, World!", result);
}
```

### Generator Snapshot Tests

```csharp
public class MyGeneratorTests : GeneratorTestBase
{
    [Fact]
    public async Task TestName()
    {
        var source = """
            using Foundatio.Mediator;

            public record Ping(string Message);

            public class PingHandler
            {
                public string Handle(Ping msg) => msg.Message + " Pong";
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}
```

After generator changes, carefully review snapshot diffs before accepting.

## Validation Checklist

Before marking work complete:

- [ ] `dotnet build Foundatio.Mediator.slnx` succeeds with no new warnings
- [ ] `dotnet test Foundatio.Mediator.slnx` passes all tests
- [ ] Public API changes are backward-compatible (or flagged as breaking)
- [ ] XML doc comments added for new public APIs
- [ ] Documentation updated in `docs/` for new features

## Error Handling

### Handler Errors

Prefer `Result<T>` over exceptions for business logic:

```csharp
public Result<User> Handle(GetUser query)
{
    var user = _repository.Find(query.Id);

    if (user == null)
        return Result.NotFound($"User {query.Id} not found");

    if (!user.IsActive)
        return Result.Forbidden("Account disabled");

    return user;  // Implicit conversion to Result<User>
}
```

### Validation Errors

Throw `ArgumentNullException`, `ArgumentException`, `InvalidOperationException` with clear messages for programmatic errors.

## Security

- Validate all inputs with guard clauses
- Never log passwords, tokens, keys, or PII
- Use `Result.Unauthorized()` or `Result.Forbidden()` instead of throwing
- Sanitize data before logging in middleware

## Resources

| Resource | Purpose |
|----------|---------|
| [docs/](docs/) | Full VitePress documentation |
| [samples/ConsoleSample/](samples/ConsoleSample/) | Basic usage examples |
| [samples/CleanArchitectureSample/](samples/CleanArchitectureSample/) | Modular monolith architecture |
| [docs/guide/configuration.md](docs/guide/configuration.md) | All configuration options |
| [docs/guide/clean-architecture.md](docs/guide/clean-architecture.md) | Clean Architecture patterns |
| [docs/guide/troubleshooting.md](docs/guide/troubleshooting.md) | Common issues and solutions |
