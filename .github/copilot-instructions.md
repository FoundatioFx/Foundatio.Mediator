# 🧠 Lightweight Mediator Library with Source Generators

## 🎯 Project Goal  
Build a fast, convention-based C# mediator library using incremental source generators and source interceptors.  
- **No interfaces or base classes required**
- **Compile-time handler discovery**
- **As close to direct method call performance as possible**

---

## 👤 Target Users

- Developers using the mediator in vertical slice .NET projects
- Prefer minimal boilerplate and full DI support

---

## 🧱 Core Features

### 1. Handler Discovery (Convention-Based)
- Handler classes must end with `Handler` or `Consumer`
- Valid method names: `Handle`, `Handles`, `HandleAsync`, `HandlesAsync`, `Consume`, `Consumes`, `ConsumeAsync`, `ConsumesAsync`
- Method signature:
  - First parameter = **message**
  - Remaining parameters = resolved via DI
  - Known args like `CancellationToken` are provided by the mediator

### 2. Incremental Source Generator
- Uses incremental source generators for:
  - Discovering handlers
  - Emitting dispatch code
  - Validating signatures
- Based on this project [NetEscapades.EnumGenerators](https://github.com/andrewlock/NetEscapades.EnumGenerators)
- Based on this article series [Creating a source generator](https://andrewlock.net/series/creating-a-source-generator/)
- Set `EmitCompilerGeneratedFiles` to `true` and `CompilerGeneratedFilesOutputPath` to `Generated` to specify output directory for generated files and see what the generator produces.

### 3. DI Integration
- Uses `Microsoft.Extensions.DependencyInjection`
- Supports:
  - Constructor injection
  - Parameter injection for handler methods and middleware
- Generate a handler class for each discovered handler that wraps the discovered handler method and implements an `IHandler<TMessage>`
  - The generated handler class is registered in the DI container as `IHandler<TMessage>`
  - `IHandler<TMessage>` is a simple interface with a single method `ValueTask HandleAsync(TMessage message, CancellationToken cancellationToken)`
- When discovering handlers at runtime, call DI `GetServices` to resolve all handlers for a message type

### 4. Lightweight Dispatch

#### Supported APIs
- `Invoke(message, CancellationToken = default)`
- `Invoke<TResponse>(message, CancellationToken = default)`
- `InvokeAsync(message, CancellationToken = default)`
- `InvokeAsync<TResponse>(message, CancellationToken = default)`

#### Behavior
- Handlers can return **any type**
- Compile-time error if a sync method is used but only an async handler exists
- Invoke calls are for exactly one handler
  - Run-time and compile-time error if no handler exists for a message type
  - Run-time and compile-time error if more than one handler exists for a message type that is invoked
- Uses generated code for near-zero overhead
- Performance is critical and the mediator should be able to handle 1000s of handler types with minimal overhead

### 5. Publish Support
- `Publish(message, CancellationToken = default)`
- `PublishAsync(message, CancellationToken = default)`
- All notification handlers are called inline
- Publish calls are for zero to many handlers
- Multiple handlers for the message type are all guaranteed to be called
- Global option to:
  - Run in parallel or sequentially
  - If any fail, all are attempted, and the first exception is thrown

### 6. Middleware Support
- Supports custom middleware classes with:
  - `Before(...)` / `BeforeAsync(...)`
  - `After(...)` / `AfterAsync(...)`
  - `Finally(...)` / `FinallyAsync(...)`
- `Before` can:
  - Return object/tuple for use in `After`/`Finally`
  - Return `HandlerResult` to short-circuit handler execution
- Middleware methods can access:
  - The message
  - `CancellationToken`
  - DI-resolved services

### 7. Cross-Project Support
- Works across multiple assemblies in vertical slice architectures
- Uses metadata references / MSBuild context

### 8. Compile-Time Safety
- Source generator enforces:
  - One handler per message
  - Valid signatures
  - Async method enforcement
  - Middleware conformance
- Generates helpful diagnostics for misconfigurations

### 9. Tuple Return Support & Cascading Messages
- Handlers can return:
  - A single object
  - A tuple with multiple values
- When using `Invoke<TResponse>()`:
  - The mediator extracts the value matching `TResponse`
  - Remaining values are treated as **cascading messages** and automatically published
  - `Invoke` does **not return** until all cascading messages are published and fully handled

---

## 🧪 Development & Testing Guidelines

### Testing Strategy
- **Prefer tests with logging output over sample projects** for experimentation and debugging
- Use `Foundatio.Xunit` with `TestWithLoggingBase` for enhanced test visibility
- Add comprehensive logging to handlers using `ILogger<T>` dependency injection
- Tests provide better isolation, repeatability, and detailed output for debugging

### Logging Infrastructure
- Tests inherit from `TestWithLoggingBase` for structured logging output
- Handlers should inject `ILogger<T>` for detailed execution tracing
- Use logging to debug source generator discovery issues and handler execution flow
- Logging output shows both test-level and handler-level execution details

### Debugging Source Generator Issues
- Create diagnostic tests to inspect service registrations with `AddMediator()`
- Set `EmitCompilerGeneratedFiles` to `true` and `CompilerGeneratedFilesOutputPath` to `Generated` to specify output directory for generated files and see what the generator produces.
- Use logging to verify which handlers are discovered vs ignored
- Single handlers work reliably; multiple handlers for same message type may need investigation
- Test with simplified handler signatures first (method DI only, no constructor DI)

---

## ⚙️ Tech Stack

- .NET 9 or later
- Incremental Source Generators
- (Optional) Source Interceptors
- Microsoft.Extensions.DependencyInjection

---

## ✅ Implementation Checklist

- [x] Define handler discovery by class and method naming convention
- [x] Discover handlers across all referenced projects
- [x] Generate dispatch logic for:
  - [x] `Invoke(...)`
  - [x] `Invoke<TResponse>(...)`
  - [x] `InvokeAsync(...)`
  - [x] `InvokeAsync<TResponse>(...)`
- [x] Generate `Publish` / `PublishAsync` logic
- [x] Support `CancellationToken` injection
- [x] Support full dependency injection for handler methods
- [x] Emit compile-time error if async-only handler is used with sync call
- [ ] Add global option for `Publish` to run handlers in parallel or sequentially
- [ ] Add middleware support:
  - [ ] `Before`, `After`, and `Finally` lifecycle
  - [ ] Pass return value from `Before` into `After`/`Finally`
  - [ ] Support short-circuiting with `HandlerResult`
  - [ ] Inject message, `CancellationToken`, and services
- [x] DI registration for handlers and middleware
- [ ] Tuple return value support:
  - [ ] Match and return expected `TResponse`
  - [ ] Publish remaining values as cascading messages
  - [ ] Ensure `Invoke` waits until cascading messages are completed
- [x] Emit compile-time diagnostics for:
  - [x] Missing handler
  - [x] Multiple handlers
  - [x] Invalid method signature
  - [x] Async enforcement
  - [ ] Middleware validation
- [ ] Optional: add source interceptor support for optimized call-sites
- [x] Provide sample app using vertical slice pattern
- [ ] Benchmark vs MediatR, Wolverine, etc.
