# Agent Guidelines for Foundatio.Mediator

You are an expert .NET engineer working on Foundatio.Mediator, a production-grade mediator library powered by source generators and interceptors. Your changes must maintain backward compatibility, performance, and reliability. Approach each task methodically: research existing patterns, make surgical changes, and validate thoroughly.

**Craftsmanship Mindset**: Every line of code should be intentional, readable, and maintainable. Write code you'd be proud to have reviewed by senior engineers. Prefer simplicity over cleverness. When in doubt, favor explicitness and clarity.

## Repository Overview

Foundatio.Mediator is a high-performance mediator library for .NET that achieves near-direct call performance through compile-time code generation:

- **Runtime** (`src/Foundatio.Mediator.Abstractions/`) - Core abstractions, interfaces, and runtime support (`IMediator`, `Result<T>`, middleware)
- **Generators** (`src/Foundatio.Mediator/`) - Source generators and analyzers that emit handler wrappers and interceptor attributes
- **Convention-Based Discovery** - Handlers discovered by class name (`*Handler`, `*Consumer`) or explicit `[Handler]` attribute
- **Middleware Pipeline** - Before/After/Finally hooks with state passing for cross-cutting concerns
- **Result Pattern** - Rich status handling without exceptions via `Result<T>`
- **Tuple Returns** - Automatic cascading messages for event-driven patterns

**Key innovation**: Zero runtime reflection. All handler dispatch is resolved at compile time via source generators that scan for handlers and emit strongly-typed wrappers with optional C# 11+ interceptor attributes for direct call redirection.

Design principles: **convention-over-configuration**, **compile-time safety**, **near-direct call performance**, **testable handlers**.

## Quick Start

```bash
# Clean (recommended before rebuilding generators)
dotnet clean Foundatio.Mediator.slnx

# Build (triggers source generators)
dotnet build Foundatio.Mediator.slnx

# Test (validate ALL changes)
dotnet test Foundatio.Mediator.slnx

# Run benchmarks
cd benchmarks/Foundatio.Mediator.Benchmarks && dotnet run -c Release -- foundatio

# Run samples
cd samples/ConsoleSample && dotnet run

# Format code
dotnet format Foundatio.Mediator.slnx
```

**Workflow**: After making code changes, ALWAYS run `dotnet build` then `dotnet test` to validate your work before considering the task complete.

## Project Structure

```text
src/
├── Foundatio.Mediator/                # Source generators
│   ├── MediatorGenerator.cs           # Main generator orchestrator
│   ├── HandlerAnalyzer.cs             # Discovers handler classes
│   ├── HandlerGenerator.cs            # Emits handler wrappers
│   ├── MiddlewareAnalyzer.cs          # Discovers middleware
│   ├── CallSiteAnalyzer.cs            # Finds mediator call sites
│   ├── InterceptsLocationGenerator.cs # Emits interceptor attributes
│   └── Foundatio.Mediator.props       # MSBuild configuration options
└── Foundatio.Mediator.Abstractions/   # Runtime library
    ├── IMediator.cs                   # Core mediator interface
    ├── Mediator.cs                    # Default implementation
    ├── Result.cs, Result.Generic.cs   # Result pattern types
    ├── HandlerRegistration.cs         # DI lookup metadata
    ├── HandlerResult.cs               # Middleware flow control
    ├── MediatorConfiguration.cs       # Runtime config
    └── HandlerExecutionInfo.cs        # Execution context
tests/
└── Foundatio.Mediator.Tests/          # Unit and integration tests
    ├── GeneratorTestBase.cs           # Base class for generator tests
    ├── BasicHandlerGenerationTests.cs # Generator snapshot tests
    └── Integration/                   # Integration test examples
samples/
├── ConsoleSample/                     # Working console example
│   ├── Handlers/                      # Handler examples
│   ├── Middleware/                    # Middleware examples
│   └── Messages/                      # Message definitions
└── ModularMonolithSample/             # Multi-module architecture example
benchmarks/
└── Foundatio.Mediator.Benchmarks/     # Performance benchmarks
docs/                                  # VitePress documentation site
```

## Coding Standards

### Style & Formatting

- Follow `.editorconfig` rules and [Microsoft C# conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Run `dotnet format` to auto-format code
- Match existing file style; minimize diffs
- Keep comments minimal—only for complex logic or non-obvious intent

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

### Architecture Patterns

- **Convention-based discovery**: Handlers discovered by naming convention (`*Handler`, `*Consumer`) or explicit attributes
- **Dependency Injection**: Use constructor injection; handlers resolved via `IServiceProvider`
- **Naming**: `Foundatio.Mediator.[Feature]` for projects, handlers end with `Handler` or `Consumer`
- **Compile-time generation**: Source generators emit strongly-typed wrapper code

### Code Quality

- Write complete, runnable code—no placeholders, TODOs, or `// existing code...` comments
- Use modern C# features: pattern matching, nullable references, `is` expressions, target-typed `new()`
- Follow SOLID, DRY principles; remove unused code and parameters
- Clear, descriptive naming; prefer explicit over clever
- Use `ConfigureAwait(false)` in library code (not in tests)
- Prefer `ValueTask<T>` for hot paths that may complete synchronously
- Always dispose resources: use `using` statements or `IAsyncDisposable`
- Handle cancellation tokens properly: check `token.IsCancellationRequested`, pass through call chains

### Common Patterns

- **Async suffix**: All async methods end with `Async` (e.g., `HandleAsync`, `InvokeAsync`)
- **CancellationToken**: Last parameter, defaulted to `default` in public APIs
- **Logging**: Use structured logging with `ILogger`, log at appropriate levels
- **Exceptions**: For handler errors, prefer `Result<T>` pattern over exceptions. Throw `ArgumentNullException`, `ArgumentException`, `InvalidOperationException` with clear messages for validation errors.

### Single Responsibility

- Each class has one reason to change
- Methods do one thing well; extract when doing multiple things
- Keep files focused: one primary type per file
- Separate concerns: don't mix I/O, business logic, and presentation
- If a method needs a comment explaining what it does, it should probably be extracted

### Performance Considerations

- **Avoid allocations in hot paths**: Use `Span<T>`, `Memory<T>`, pooled buffers
- **Prefer structs for small, immutable types**: But be aware of boxing
- **Cache expensive computations**: Use `Lazy<T>` or explicit caching
- **Profile before optimizing**: Don't guess—measure with benchmarks
- **Consider concurrent access**: Use `ConcurrentDictionary`, `Interlocked`, or proper locking
- **Avoid async in tight loops**: Consider batching or `ValueTask` for hot paths
- **Dispose resources promptly**: Don't hold connections/handles longer than needed

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

// Control DI lifetime with the Lifetime property
[Middleware(Order = 2, Lifetime = MediatorLifetime.Scoped)]
public class ScopedMiddleware
{
    public void Before(object message) { }
}
```

**Cross-Assembly Limitation**: Middleware must be defined in the same project as handlers. The source generator only has access to the current project's source code. Use linked files (`<Compile Include="..." Link="..." />` in `.csproj`) to share middleware across projects, and declare middleware classes as `internal` to avoid type conflicts.

### Execution Semantics

- **Invoke/InvokeAsync**: Requires EXACTLY one handler. Throws if zero or multiple handlers found.
- **PublishAsync**: Runs ALL applicable handlers (exact type, interfaces, base classes) in parallel.

## Making Changes

### Before Starting

1. **Gather context**: Read related files, search for similar implementations, understand the full scope
2. **Research patterns**: Find existing usages of the code you're modifying using grep/semantic search
3. **Understand completely**: Know the problem, side effects, and edge cases before coding
4. **Plan the approach**: Choose the simplest solution that satisfies all requirements
5. **Check dependencies**: Verify you understand how changes affect dependent code

### Pre-Implementation Analysis

Before writing any implementation code, think critically:

1. **What could go wrong?** Consider race conditions, null references, edge cases, resource exhaustion
2. **What are the failure modes?** Network failures, timeouts, out-of-memory, concurrent access
3. **What assumptions am I making?** Validate each assumption against the codebase
4. **Is this the root cause?** Don't fix symptoms—trace to the core problem
5. **Will this scale?** Consider performance under load, memory allocation patterns
6. **Is there existing code that does this?** Search before creating new utilities

### Test-First Development

**Always write or extend tests before implementing changes:**

1. **Find existing tests first**: Search for tests covering the code you're modifying
2. **Extend existing tests**: Add test cases to existing test classes/methods when possible for maintainability
3. **Write failing tests**: Create tests that demonstrate the bug or missing feature
4. **Implement the fix**: Write minimal code to make tests pass
5. **Refactor**: Clean up while keeping tests green
6. **Verify edge cases**: Add tests for boundary conditions and error paths

**Why extend existing tests?** Consolidates related test logic, reduces duplication, improves discoverability, maintains consistent test patterns.

### While Coding

- **Minimize diffs**: Change only what's necessary, preserve formatting and structure
- **Preserve behavior**: Don't break existing functionality or change semantics unintentionally
- **Build incrementally**: Run `dotnet build` after each logical change to catch errors early
- **Test continuously**: Run `dotnet test` frequently to verify correctness
- **Match style**: Follow the patterns in surrounding code exactly

### Validation

Before marking work complete, verify:

1. **Builds successfully**: `dotnet build Foundatio.Mediator.slnx` exits with code 0
2. **All tests pass**: `dotnet test Foundatio.Mediator.slnx` shows no failures
3. **No new warnings**: Check build output for new compiler warnings
4. **API compatibility**: Public API changes are intentional and backward-compatible when possible
5. **Documentation updated**: XML doc comments added/updated for public APIs
6. **Interface documentation**: Update interface definitions and docs with any API changes
7. **Feature documentation**: Add entries to [docs/](docs/) folder for new features or significant changes
8. **Breaking changes flagged**: Clearly identify any breaking changes for review

### Error Handling

- **Validate inputs**: Check for null, empty strings, invalid ranges at method entry
- **Fail fast**: Throw exceptions immediately for invalid arguments (don't propagate bad data)
- **Meaningful messages**: Include parameter names and expected values in exception messages
- **Don't swallow exceptions**: Log and rethrow, or let propagate unless you can handle properly
- **Use guard clauses**: Early returns for invalid conditions, keep happy path unindented

## Security

- **Validate all inputs**: Use guard clauses, check bounds, validate formats before processing
- **Sanitize external data**: Never trust data from queues, caches, or external sources
- **Avoid injection attacks**: Use parameterized queries, escape user input, validate file paths
- **No sensitive data in logs**: Never log passwords, tokens, keys, or PII
- **Use secure defaults**: Default to encrypted connections, secure protocols, restricted permissions
- **Follow OWASP guidelines**: Review [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- **Dependency security**: Check for known vulnerabilities before adding dependencies
- **No deprecated APIs**: Avoid obsolete cryptography, serialization, or framework features
- **Mediator-specific**: Always validate message parameters; use middleware for cross-cutting security (authentication, authorization); return `Result.Unauthorized()` or `Result.Forbidden()` instead of throwing
- **Sanitize before logging**: In middleware, sanitize data before logging to avoid leaking sensitive information

## Testing

### Philosophy: Battle-Tested Code

Tests are not just validation—they're **executable documentation** and **design tools**. Well-tested code is:

- **Trustworthy**: Confidence to refactor and extend
- **Documented**: Tests show how the API should be used
- **Resilient**: Edge cases are covered before they become production bugs

### Framework

- **xUnit** as the primary testing framework
- **Verify library** for snapshot testing of generated code
- Follow [Microsoft unit testing best practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)

### Test-First Workflow

1. **Search for existing tests**: `dotnet test --filter "FullyQualifiedName~MethodYouAreChanging"`
2. **Extend existing test classes**: Add new `[Fact]` or `[Theory]` cases to existing files
3. **Write the failing test first**: Verify it fails for the right reason
4. **Implement minimal code**: Just enough to pass the test
5. **Add edge case tests**: Null inputs, empty collections, boundary values, concurrent access
6. **Run full test suite**: Ensure no regressions

### Generator Test Workflow

When modifying source generators:

1. Make code changes to generator files
2. Run `dotnet test` immediately
3. Fix any failures before proceeding
4. Review snapshot diffs carefully if generator tests fail
5. Accept snapshots only after verifying the generated code is correct

### Test Principles (FIRST)

- **Fast**: Tests execute quickly
- **Isolated**: No dependencies on external services or execution order
- **Repeatable**: Consistent results every run
- **Self-checking**: Tests validate their own outcomes
- **Timely**: Write tests alongside code

### Naming Convention

Use the pattern: `MethodName_StateUnderTest_ExpectedBehavior`

Examples:

- `GenerateHandler_WithSimpleMessage_CreatesValidWrapper`
- `InvokeAsync_WithRegisteredHandler_ReturnsExpectedResult`
- `BeforeMiddleware_WithValidState_PassesStateToAfter`

### Test Structure

Follow the AAA (Arrange-Act-Assert) pattern:

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
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message) : IQuery<string>;

            public class PingHandler {
                public Task<string> HandleAsync(Ping message, CancellationToken ct)
                    => Task.FromResult(message.Message + " Pong");
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}
```

### Integration Testing

- Prefer unit tests over integration tests
- When integration tests are needed, use existing message types from `Integration/` directory
- See [tests/Foundatio.Mediator.Tests/Integration/ScopedDependencyTests.cs](tests/Foundatio.Mediator.Tests/Integration/ScopedDependencyTests.cs) for examples

### Running Tests

```bash
# All tests
dotnet test Foundatio.Mediator.slnx

# Specific test file
dotnet test --filter "FullyQualifiedName~BasicHandlerGenerationTests"

# With logging
dotnet test --logger "console;verbosity=detailed"
```

## Debugging

1. **Reproduce** with minimal steps
2. **Understand** the root cause before fixing
3. **Test** the fix thoroughly
4. **Document** non-obvious fixes in code if needed

## Resilience & Reliability

- **Expect failures**: Network calls fail, resources exhaust, concurrent access races
- **Timeouts everywhere**: Never wait indefinitely; use cancellation tokens
- **Retry with backoff**: Use exponential backoff with jitter for transient failures
- **Graceful degradation**: Return cached data, default values, or partial results when appropriate
- **Idempotency**: Design operations to be safely retryable
- **Resource limits**: Bound queues, caches, and buffers to prevent memory exhaustion

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
   <MediatorDefaultHandlerLifetime>Transient|Scoped|Singleton</MediatorDefaultHandlerLifetime>
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
<MediatorDefaultHandlerLifetime>None|Transient|Scoped|Singleton</MediatorDefaultHandlerLifetime>

<!-- Auto-register middleware in DI (default: None) -->
<MediatorDefaultMiddlewareLifetime>None|Transient|Scoped|Singleton</MediatorDefaultMiddlewareLifetime>

<!-- Disable OpenTelemetry tracing (default: false) -->
<MediatorDisableOpenTelemetry>true|false</MediatorDisableOpenTelemetry>

<!-- Disable conventional handler discovery (default: false) -->
<!-- When true, only handlers with IHandler interface or [Handler] attribute are discovered -->
<MediatorDisableConventionalDiscovery>true|false</MediatorDisableConventionalDiscovery>
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

  Foundatio.Mediator.Abstractions/ # Runtime library
    IMediator.cs                   # Core mediator interface
    Mediator.cs                    # Default implementation
    HandlerRegistration.cs         # DI lookup metadata
    HandlerResult.cs               # Middleware flow control
    IResult.cs, Result.cs          # Result pattern types
    MediatorConfiguration.cs       # Runtime config
    HandlerExecutionInfo.cs        # Execution context

tests/Foundatio.Mediator.Tests/
  GeneratorTestBase.cs             # Base class for generator tests
  BasicHandlerGenerationTests.cs   # Example generator tests
  Integration/                     # Integration test examples

samples/
  ConsoleSample/                   # Working example
    Handlers/Handlers.cs           # Handler examples
    Middleware/LoggingMiddleware.cs # Middleware example
    Program.cs, ServiceConfiguration.cs
```

## Advanced Patterns

### Cascading Messages (Tuple Returns)

First element = response; remaining non-null elements = auto-published events:

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

## Resources

- [README.md](README.md) - Overview and feature list
- [docs/](docs/) - Full documentation (VitePress)
- [samples/ConsoleSample/](samples/ConsoleSample/) - Complete working examples
- [samples/ModularMonolithSample/](samples/ModularMonolithSample/) - Multi-module architecture
- [tests/Foundatio.Mediator.Tests/Integration/](tests/Foundatio.Mediator.Tests/Integration/) - Real-world scenarios
- [benchmarks/](benchmarks/) - Performance testing
