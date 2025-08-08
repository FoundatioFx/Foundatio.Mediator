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

## Build/run
- From repo root: dotnet build; dotnet test (tests/Foundatio.Mediator.Tests/).
- Sample app: `samples/ConsoleSample/` (see `Program.cs`, `ServiceConfiguration.cs`).

## Handlers (discovery/execution)
- Class ends with `Handler` or `Consumer` (see `src/Foundatio.Mediator/HandlerAnalyzer.cs`).
- Public method name is one of: Handle/HandleAsync/Handles/HandlesAsync/Consume/ConsumeAsync/Consumes/ConsumesAsync.
- First parameter is the message; other parameters are DI-resolved (incl. CancellationToken).
- Invoke/InvokeAsync require exactly one handler. PublishAsync runs all applicable (exact type, interfaces, base classes) inline and in parallel (see `src/Foundatio.Mediator.Abstractions/Mediator.cs`).
- Tuple returns cascade: first element is the response; remaining non-null items are auto-published before returning.

## Middleware
- Class ends with `Middleware` (see `src/Foundatio.Mediator/MiddlewareAnalyzer.cs`). Methods: Before(Async), After(Async), Finally(Async).
- Before may return value/tuple; these are type-matched into After/Finally parameters.
- Short-circuit by returning `HandlerResult.ShortCircuit(value)` from Before; handler is skipped and `value` is returned.
- Order with `[FoundatioOrder(int)]`; lower runs earlier in Before and later in After/Finally.

## DI and generation
- Register: `services.AddMediator()` (see `src/Foundatio.Mediator.Abstractions/MediatorExtensions.cs`). Generator emits `[assembly: FoundatioHandlerModule]` and DI `HandlerRegistration` per message.
- Handlers are not auto-registered; wrappers create instances via `ActivatorUtilities` if not in DI. Register handlers to control lifetime.
- Middleware lifetime: `Mediator.GetOrCreateMiddleware<T>` caches if not in DI; register to control lifetime.

## Interceptors toggle
- Default ON. Disable via `<DisableMediatorInterceptors>true</DisableMediatorInterceptors>` (see `src/Foundatio.Mediator/Foundatio.Mediator.props` and `.targets`).

## Reference map
- Runtime: `src/Foundatio.Mediator.Abstractions/` (IMediator, Result, HandlerRegistration, MediatorConfiguration)
- Generators: `src/Foundatio.Mediator/` (MediatorGenerator, HandlerGenerator, DIRegistrationGenerator)
- Samples: `samples/ConsoleSample/`
- Tests: `tests/Foundatio.Mediator.Tests/`

## Testing approach
- Iterate using tests in `tests/Foundatio.Mediator.Tests/`—do not create ad-hoc console apps or sample projects.
- Prefer unit tests and snapshot verification for source generator output; keep integration tests minimal. When creating integration tests, use existing message types and handlers if possible.
- After edits, run the test task and fix failures before moving on.

## Commenting style
- Keep comments to a minimum.
- Only add comments when intent is not obvious or when explaining complex logic, tricky edge cases, or non-trivial performance/interop considerations.
- Prefer clear naming and small, self-explanatory functions over explanatory comments.
- Avoid boilerplate and restating what the code already says.

