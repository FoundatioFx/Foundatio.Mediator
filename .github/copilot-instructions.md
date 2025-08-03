# 🧠 Lightweight Mediator Library with Source Generators

## 🎯 Project Goal

Build a fast, convention-based C# mediator library using incremental source generators and source interceptors.

* **No interfaces or base classes required**
* **Compile-time handler discovery**
* **As close to direct method call performance as possible**
* **High performance: capable of dispatching thousands of handlers efficiently**

---

## 👤 Target Users

* Developers using the mediator in vertical slice .NET projects
* Prefer minimal boilerplate and full DI support

---

## 🧱 Core Features

### 1. Handler Discovery (Convention-Based)

* Handler classes must end with `Handler` or `Consumer`
* Valid method names:

  * `Handle`, `Handles`, `HandleAsync`, `HandlesAsync`
  * `Consume`, `Consumes`, `ConsumeAsync`, `ConsumesAsync`
* Method signature:

  * First parameter = **message**
  * Remaining parameters = resolved via DI
  * Known args like `CancellationToken` are injected by the mediator

---

### 2. Incremental Source Generator

* Uses C# incremental source generators for:

  * Discovering handler methods
  * Emitting static handler wrappers
  * Validating method signatures
* Generator inspired by:

  * [NetEscapades.EnumGenerators](https://github.com/andrewlock/NetEscapades.EnumGenerators)
  * [Creating a Source Generator series](https://andrewlock.net/series/creating-a-source-generator/)
* Use `EmitCompilerGeneratedFiles=true` and `CompilerGeneratedFilesOutputPath=Generated` for output inspection

---

### 3. New Static Handler Codegen Strategy

* **No `IHandler<TMessage>` interface**
* For each discovered handler, generate a static async method:

  ```csharp
  public static ValueTask<object> HandleAsync(IMediator mediator, object message, CancellationToken token, Type? responseType)
  ```
* The method:

  * Casts `message` to expected type
  * Resolves class and method parameters via DI
  * Calls the handler method
  * Handles return values (including tuple/cascading)

---

### 4. Handler Registration & Resolution

* For each handler, register a singleton instance of `HandlerRegistration`:

  * Keyed by the fully qualified type name of the message (e.g., `Namespace.MyMessage`)
  * Stores a delegate:

    ```csharp
    Func<IMediator, object, CancellationToken, Type?, ValueTask<object>> HandleAsync
    ```
* Mediator resolves `IEnumerable<HandlerRegistration>` from DI
* Filters by message type name
* Invokes matching handlers using the delegate

---

### 5. Invoke APIs

* Supported variants:

  * `Invoke(message, CancellationToken = default)`
  * `Invoke<TResponse>(message, CancellationToken = default)`
  * `InvokeAsync(message, CancellationToken = default)`
  * `InvokeAsync<TResponse>(message, CancellationToken = default)`
* **Only one handler** is allowed per message type
* **Compile-time and runtime validation**:

  * No handler → error
  * Multiple handlers → error
  * Sync method used with async-only handler → compile-time error

---

### 6. Publish APIs

* Supported variants:

  * `PublishAsync(message, CancellationToken = default)`
* **Zero to many handlers** per message
* All handlers are called inline
* Global execution mode:

  * Sequential (one-at-a-time)
  * Parallel (in `Task.WhenAll`)
* If one handler throws:

  * All others still run
  * If one handler throws, the exception is rethrown after all handlers complete
  * If multiple handlers throw, an `AggregateException` is thrown with all exceptions

---

### 7. Tuple Return Support & Cascading Messages

* Handlers can return:

  * A single object
  * A tuple with multiple values
* When using `Invoke<TResponse>()`:

  * The mediator extracts the value matching `TResponse`
  * Remaining values are **published automatically** as cascading messages
  * `Invoke` does **not return** until all cascading messages are handled

---

### 8. Middleware Support

* Middleware classes support:

  * `Before(...)` / `BeforeAsync(...)`
  * `After(...)` / `AfterAsync(...)`
  * `Finally(...)` / `FinallyAsync(...)`
* `Before` can:

  * Return object/tuple to pass to `After` and `Finally`
  * Return a `HandlerResult` to short-circuit handler execution
* Methods may take:

  * The message
  * `CancellationToken`
  * DI-resolved services
* `After` runs only on successful handler completion
* `Finally` runs **always**, regardless of success/failure

---

### 9. Cross-Project Support

* Must support handler discovery across multiple projects (vertical slice architecture)
* Uses metadata references / MSBuild project graph
* Works with SDK-style projects

---

### 10. Compile-Time Safety

* Source generator emits diagnostics for:

  * Invalid method signatures
  * More than one handler for a single message (for `Invoke`)
  * Missing handler
  * Async-only handler used with sync API
  * Middleware misconfiguration

---

## 🧪 Development & Testing Guidelines

### Testing Strategy

* Prefer test-based dev using `Foundatio.Xunit`
* Use `TestWithLoggingBase` for rich test logging
* Avoid large samples for dev iteration — rely on tight log-driven test loops

### Logging

* Handlers and middleware should support `ILogger<T>` injection
* Log:

  * Handler execution start/finish
  * Middleware phases
  * Tuple/cascading message flow
  * Source generation metadata (discovery, errors)
