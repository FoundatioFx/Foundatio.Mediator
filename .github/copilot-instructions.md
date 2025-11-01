# Foundatio.Mediator – AI agent guide

Use this when coding in this repo. Keep advice specific; reference files/paths.

## Key Principles

All contributions must respect existing formatting and conventions specified in the `.editorconfig` file. You are a distinguished engineer and are expected to deliver high-quality code that adheres to the guidelines in the instruction files.

Let's keep pushing for clarity, usability, and excellence—both in code and user experience.

**See also:**

- [General Coding Guidelines](instructions/general.instructions.md)
- [Testing Guidelines](instructions/testing.instructions.md)

## Big picture
- Convention-based mediator for .NET with source generators + C# interceptors.
- Two parts: runtime (`src/Foundatio.Mediator.Abstractions`) and generators/analyzers (`src/Foundatio.Mediator`).
- Dispatch: same-assembly calls are intercepted to generated static wrappers; cross-assembly/publish uses DI `HandlerRegistration` keyed by message type.
- **Key innovation**: Zero runtime reflection. Generators scan for handlers at compile time and emit strongly-typed wrapper classes with optional interceptor attributes for direct call redirection.

## Architecture deep dive

### Source generator pipeline (see `MediatorGenerator.cs`)
1. **HandlerAnalyzer**: Scans classes ending in `Handler`/`Consumer` or marked with `[Handler]` attribute
2. **MiddlewareAnalyzer**: Scans classes ending in `Middleware` for Before/After/Finally methods
3. **CallSiteAnalyzer**: Finds all `mediator.InvokeAsync()`/`PublishAsync()` call sites
4. **HandlerGenerator**: Emits wrapper classes like `MessageType_Handler` with static `HandleAsync` methods
5. **InterceptsLocationGenerator**: Emits `[InterceptsLocation]` attributes to redirect calls (C# 11+ only)
6. **DIRegistrationGenerator**: Emits `HandlerRegistration` entries for cross-assembly and publish scenarios

### Interceptors vs fallback
- **Same assembly + C# 11+**: Calls intercepted at compile time to generated static wrappers (near-zero overhead)
- **Cross-assembly or disabled**: Falls back to DI lookup via `HandlerRegistration` dictionary keyed by message type
- Toggle: `<MediatorDisableInterceptors>true</MediatorDisableInterceptors>` in `.csproj` (see `Foundatio.Mediator.props`)
- Requires `LanguageVersion` at least `CSharp11` and interceptors not explicitly disabled

### Handler discovery rules (see `HandlerAnalyzer.cs:IsMatch`)
- Class name ends with `Handler` or `Consumer`, OR
- Class implements `IHandler`, OR
- Class has `[Handler]` attribute, OR
- Class has method with `[Handler]` attribute
- Excluded: Generated classes in `Foundatio.Mediator` namespace ending in `_Handler`
- Excluded: Classes with `[FoundatioIgnore]` attribute

### Handler method conventions
- Method names: `Handle`, `HandleAsync`, `Handles`, `HandlesAsync`, `Consume`, `ConsumeAsync`, `Consumes`, `ConsumesAsync`
- First parameter = message; remaining parameters resolved from DI (including `CancellationToken`)
- Can be instance or static methods
- Return types: void, Task, ValueTask, TResponse, Task<TResponse>, ValueTask<TResponse>, Result<T>, or tuple for cascading

## Build/run
- From repo root: `dotnet build` (runs generators); `dotnet test`
- VS Code tasks: "build solution" (default build), "run tests" (default test)
- Sample app: `samples/ConsoleSample/` (see `Program.cs`, `ServiceConfiguration.cs`, `Handlers/Handlers.cs`)
- Benchmarks: `benchmarks/Foundatio.Mediator.Benchmarks/` (run with `dotnet run -c Release`)

## Handlers (discovery/execution)

### Discovery conventions
- Class ends with `Handler` or `Consumer` (see `src/Foundatio.Mediator/HandlerAnalyzer.cs:IsMatch`)
- Public method name is one of: Handle/HandleAsync/Handles/HandlesAsync/Consume/ConsumeAsync/Consumes/ConsumesAsync
- First parameter is the message; other parameters are DI-resolved (incl. CancellationToken)

### Execution semantics
- `Invoke/InvokeAsync`: Require exactly ONE handler. Throws if zero or multiple found.
- `PublishAsync`: Runs ALL applicable handlers (exact type, interfaces, base classes) inline and in parallel (see `src/Foundatio.Mediator.Abstractions/Mediator.cs`)
- Tuple returns cascade: first element is the response; remaining non-null items are auto-published before returning (see `samples/ConsoleSample/Handlers/Handlers.cs:HandleAsync` for example)

### Handler lifetime
- Handlers are NOT auto-registered in DI. Generator creates instances via `ActivatorUtilities.CreateInstance()` if not in DI.
- To control lifetime, register handlers explicitly: `services.AddTransient<OrderHandler>()`
- Can force auto-registration with MSBuild property: `<MediatorHandlerLifetime>Transient|Scoped|Singleton</MediatorHandlerLifetime>` (see `Foundatio.Mediator.props`)

## Middleware

### Discovery and execution (see `MiddlewareAnalyzer.cs`)
- Class ends with `Middleware`
- Methods: `Before(Async)`, `After(Async)`, `Finally(Async)`
- Before executes top-to-bottom; After/Finally execute bottom-to-top
- Example: `samples/ConsoleSample/Middleware/LoggingMiddleware.cs`

### State passing
- Before may return value/tuple; these are type-matched into After/Finally parameters by type (see `samples/ConsoleSample/Middleware/LoggingMiddleware.cs` - returns `Stopwatch`)
- Middleware receives `HandlerExecutionInfo` (handler type, method, message type) as parameter

### Short-circuiting
- Return `HandlerResult.ShortCircuit(value)` from Before; handler is skipped and `value` is returned as the response
- Example: authentication middleware that returns 401 without executing handler

### Ordering
- Use `[FoundatioOrder(int)]` attribute; lower number runs earlier in Before, later in After/Finally
- Default order: 0

### Lifetime
- `Mediator.GetOrCreateMiddleware<T>` caches instances if not in DI
- Register in DI to control lifetime: `services.AddSingleton<LoggingMiddleware>()`

## Result pattern (see `IResult.cs`, `HandlerResult.cs`)

### Result<T> usage
- Built-in rich status handling: Success, Created, NotFound, Conflict, Forbidden, Unauthorized, ValidationError, etc.
- Example: `Result<Order>.Created(order, "/orders/123")` or `Result.NotFound("Order not found")`
- Check: `result.IsSuccess`, `result.Status`, `result.Message`, `result.ValidationErrors`
- See `samples/ConsoleSample/Handlers/Handlers.cs:HandleAsync` for real examples

### HandlerResult for middleware
- `HandlerResult.Continue(value)`: Pass value to After/Finally but continue handler execution
- `HandlerResult.ShortCircuit(value)`: Skip handler and return value immediately
- Implicit conversion from `Result<T>` to `HandlerResult` enables clean middleware short-circuits

## DI and generation
- Register: `services.AddMediator()` (see `src/Foundatio.Mediator.Abstractions/MediatorExtensions.cs`)
- Generator emits `[assembly: FoundatioModule]` and `HandlerRegistration` per message type
- `HandlerRegistration` keyed by `MessageTypeKey.Get(type)` for cross-assembly/publish lookup
- Handlers are not auto-registered; wrappers create instances via `ActivatorUtilities` if not in DI. Register handlers to control lifetime.

## Configuration options (MSBuild properties)

### Interceptors (see `Foundatio.Mediator.props`)
- `<MediatorDisableInterceptors>true|false</MediatorDisableInterceptors>` - Default: false (enabled)
- Requires C# 11+ language version

### Handler lifetime
- `<MediatorHandlerLifetime>None|Transient|Scoped|Singleton</MediatorHandlerLifetime>` - Default: None
- When set, auto-registers handlers with specified lifetime in generated DI code

### OpenTelemetry
- `<MediatorDisableOpenTelemetry>true|false</MediatorDisableOpenTelemetry>` - Default: false (enabled)
- Controls whether ActivitySource tracing is included in generated code

## Reference map
- Runtime: `src/Foundatio.Mediator.Abstractions/` (IMediator, Result, HandlerRegistration, MediatorConfiguration, HandlerResult)
- Generators: `src/Foundatio.Mediator/` (MediatorGenerator, HandlerGenerator, DIRegistrationGenerator, InterceptsLocationGenerator)
- Analyzers: `src/Foundatio.Mediator/` (HandlerAnalyzer, MiddlewareAnalyzer, CallSiteAnalyzer)
- Samples: `samples/ConsoleSample/` (Handlers/, Middleware/, Messages/)
- Tests: `tests/Foundatio.Mediator.Tests/` (includes snapshot verification tests)

## Testing approach

### Test philosophy
- Iterate using tests in `tests/Foundatio.Mediator.Tests/`—do not create ad-hoc console apps or sample projects
- Prefer unit tests and snapshot verification for source generator output; keep integration tests minimal
- When creating integration tests, use existing message types and handlers if possible (see `Integration/` directory)

### Snapshot verification (Verify library)
- Generator tests use `VerifyGenerated()` helper (see `GeneratorTestBase.cs`)
- Snapshots stored as `.verified.txt` files next to test files
- Example: `BasicHandlerGenerationTests.GeneratesWrapperForSimpleHandler.verified.txt`
- After generator changes, review snapshot diffs carefully before accepting

### Running tests
- VS Code: Use "run tests" task or test explorer
- Terminal: `dotnet test` from repo root
- After edits, run the test task and fix failures before moving on

## Commenting style
- Keep comments to a minimum
- Only add comments when intent is not obvious or when explaining complex logic, tricky edge cases, or non-trivial performance/interop considerations
- Prefer clear naming and small, self-explanatory functions over explanatory comments
- Avoid boilerplate and restating what the code already says

