# Handler Conventions

Foundatio Mediator uses simple naming conventions to automatically discover handlers at compile time. This eliminates the need for interfaces, base classes, or manual registration while providing excellent compile-time validation.

Alternatively, you can mark handlers explicitly using the `IHandler` marker interface or the `[Handler]` attribute. See [Explicit Handler Declaration](#explicit-handler-declaration) for details.

## Class Naming Conventions

Handler classes must end with one of these suffixes:

- `Handler`
- `Consumer`

```csharp
// ✅ Valid handler class names
public class UserHandler { }
public class OrderHandler { }
public class EmailConsumer { }
public class NotificationConsumer { }

// ❌ Invalid - won't be discovered
public class UserService { }
public class OrderProcessor { }
```

## Method Naming Conventions

Handler methods must use one of these names:

- `Handle` / `HandleAsync`
- `Handles` / `HandlesAsync`
- `Consume` / `ConsumeAsync`
- `Consumes` / `ConsumesAsync`

```csharp
public class UserHandler
{
    // ✅ All of these work
    public User Handle(GetUser query) { }
    public Task<User> HandleAsync(GetUser query) { }
    public User Handles(GetUser query) { }
    public Task<User> HandlesAsync(GetUser query) { }

    // ❌ These won't be discovered
    public User Process(GetUser query) { }
    public User Get(GetUser query) { }
}
```

## Method Signature Requirements

### First Parameter: The Message

The first parameter must be the message object:

```csharp
public class OrderHandler
{
    // ✅ Message as first parameter
    public Order Handle(CreateOrder command) { }

    // ❌ Message not first
    public Order Handle(ILogger logger, CreateOrder command) { }
}
```

### Additional Parameters: Dependency Injection

All parameters after the first are resolved via dependency injection:

```csharp
public class OrderHandler
{
    public async Task<Order> HandleAsync(
        CreateOrder command,           // ✅ Message (required first)
        IOrderRepository repository,   // ✅ Injected from DI
        ILogger<OrderHandler> logger,  // ✅ Injected from DI
        CancellationToken ct          // ✅ Automatically provided
    )
    {
        logger.LogInformation("Creating order for {CustomerId}", command.CustomerId);
        return await repository.CreateAsync(command, ct);
    }
}
```

### Supported Parameter Types

- **Any registered service** from the DI container
- **CancellationToken** - automatically provided by the mediator
- **Scoped services** - new instance per mediator invocation
- **Singleton services** - shared instance

## Return Types

Handlers can return any type:

```csharp
public class ExampleHandler
{
    // ✅ Void (fire-and-forget)
    public void Handle(LogMessage command) { }

    // ✅ Task (async fire-and-forget)
    public Task HandleAsync(SendEmail command) { }

    // ✅ Value types
    public int Handle(CalculateSum query) { }

    // ✅ Reference types
    public User Handle(GetUser query) { }

    // ✅ Generic types
    public Task<List<Order>> HandleAsync(GetOrders query) { }

    // ✅ Result types
    public Result<User> Handle(GetUser query) { }

    // ✅ Tuples (for cascading messages)
    public (User user, UserCreated evt) Handle(CreateUser cmd) { }
}
```

## Handler Types

### Static Handlers

Simple, stateless handlers can be static:

```csharp
public static class CalculationHandler
{
    public static int Handle(AddNumbers query)
    {
        return query.A + query.B;
    }

    public static decimal Handle(CalculateTax query)
    {
        return query.Amount * 0.08m;
    }
}
```

**Benefits:**

- No DI registration required (but can be registered when it is desired to control handler class lifetime)
- Zero allocation for handler instance
- Clear that no state is maintained

### Instance Handlers

For handlers requiring dependencies:

```csharp
public class UserHandler
{
    private readonly IUserRepository _repository;
    private readonly ILogger<UserHandler> _logger;

    // Constructor injection
    public UserHandler(IUserRepository repository, ILogger<UserHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<User> HandleAsync(GetUser query)
    {
        _logger.LogInformation("Getting user {UserId}", query.Id);
        return await _repository.GetByIdAsync(query.Id);
    }
}
```

**Note:** Handlers are singleton by default. Constructor dependencies are resolved once and shared across all invocations.

### Open Generic Handlers

Handlers can be declared as open generic classes and will be automatically closed for the concrete message type at runtime. This lets you build reusable handler logic that applies to many message types.

```csharp
// Generic command definitions
public record UpdateEntity<T>(T Entity);
public record UpdateRelation<TLeft, TRight>(TLeft Left, TRight Right);

// Open generic handler (single generic parameter)
public class EntityHandler<T>
{
    public Task HandleAsync(UpdateEntity<T> command, CancellationToken ct)
    {
        // process update for entity of type T
        return Task.CompletedTask;
    }
}

// Open generic handler (two generic parameters)
public class RelationHandler<TLeft, TRight>
{
    public Task HandleAsync(UpdateRelation<TLeft, TRight> command, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

// Usage
await mediator.InvokeAsync(new UpdateEntity<Order>(order));
await mediator.InvokeAsync(new UpdateRelation<User, Role>(user, role));
```

Guidelines:

- The handler class, not the method, must be generic (generic handler methods are not currently supported).
- The message type must use the handler's generic parameters (e.g., `UpdateEntity<T>` in `EntityHandler<T>`).
- Open generic handlers participate in normal invocation rules: exactly one match required for `Invoke / InvokeAsync`; multiple open generics for the same message generic definition will cause an error when invoking (publish supports multiple).

Performance Notes:

- First invocation of a new closed generic combination incurs a small reflection cost; subsequent calls are cached.
- Static middleware resolution still applies and middleware can itself be generic.

If you need a custom behavior per entity type later, you can still add a concrete handler; the more specific (closed) handler will coexist.

## Multiple Handlers in One Class

A single class can handle multiple message types:

```csharp
public class OrderHandler
{
    public Result<Order> Handle(CreateOrder command) { }
    public Result<Order> Handle(GetOrder query) { }
    public Result<Order> Handle(UpdateOrder command) { }
    public Result Handle(DeleteOrder command) { }
}
```

## Handler Lifetime Management

### Default Behavior (Singleton)

```csharp
public class UserHandler
{
    private readonly ILogger _logger; // ⚠️ Resolved once, shared across all calls

    public UserHandler(ILogger<UserHandler> logger)
    {
        _logger = logger; // Singleton dependency - OK
    }

    public User Handle(GetUser query, DbContext context) // ✅ Per-request dependency
    {
        // context is resolved fresh for each call
        return context.Users.Find(query.Id);
    }
}
```

### Explicit DI Registration

Control handler lifetime by registering in DI:

```csharp
// Scoped handlers (new instance per request)
services.AddScoped<UserHandler>();
services.AddScoped<OrderHandler>();

// Transient handlers (new instance per use)
services.AddTransient<ExpensiveHandler>();
```

### Automatic DI Registration

Use MSBuild property to auto-register handlers:

```xml
<PropertyGroup>
    <MediatorHandlerLifetime>Scoped</MediatorHandlerLifetime>
</PropertyGroup>
```

Options: `None` (default), `Singleton`, `Scoped`, `Transient`

## Handler Discovery Rules

### Assembly Scanning

The source generator scans the current assembly for:

1. **Public classes** ending with `Handler` or `Consumer`
2. **Public methods** with valid handler names
3. **First parameter** that defines the message type

### Manual Handler Discovery

Handler classes can implement the `IHandler` interface for manual discovery:

```csharp
public class UserProcessor : IHandler
{
    public User Handle(GetUser query) { } // ✅ Discovered
}
```

Handler classes and methods can be marked with the `[Handler]` attribute for manual discovery:

```csharp
public class UserProcessor
{
    [Handler]
    public User Process(GetUser query) { } // ✅ Discovered
}
```

## Compile-Time Validation

The source generator provides compile-time errors for:

### Multiple Handlers (for Invoke)

```csharp
public class Handler1
{
    public string Handle(DuplicateMessage msg) => "Handler1";
}

public class Handler2
{
    public string Handle(DuplicateMessage msg) => "Handler2";
}

// ❌ Compile-time error
await mediator.InvokeAsync<string>(new DuplicateMessage());
// Error: Multiple handlers found for message type 'DuplicateMessage'
```

### Return Type Mismatches

```csharp
public class UserHandler
{
    public string Handle(GetUser query) => "Not a user"; // Returns string
}

// ❌ Compile-time error
var user = await mediator.InvokeAsync<User>(new GetUser(1));
// Error: Handler returns 'string' but expected 'User'
```

### Async/Sync Mismatches

```csharp
public class AsyncHandler
{
    public async Task<string> HandleAsync(GetMessage query)
    {
        await Task.Delay(100);
        return "Result";
    }
}

// ❌ Compile-time error - handler is async but calling sync method
var result = mediator.Invoke<string>(new GetMessage());
// Error: Async handler found but sync method called
```

## Ignoring Handlers

Use `[FoundatioIgnore]` to exclude classes or methods:

```csharp
[FoundatioIgnore] // Entire class ignored
public class DisabledHandler
{
    public string Handle(SomeMessage msg) => "Ignored";
}

public class PartialHandler
{
    public string Handle(Message1 msg) => "Handled";

    [FoundatioIgnore] // Only this method ignored
    public string Handle(Message2 msg) => "Ignored";
}
```

## Best Practices

### 1. Use Descriptive Handler Names

```csharp
// ✅ Clear purpose
public class UserRegistrationHandler { }
public class OrderPaymentHandler { }
public class EmailNotificationConsumer { }
public class UserHandler { }

// ❌ Too generic
public class Handler { } // Handles what?
```

### 2. Group Related Operations

```csharp
// ✅ Cohesive handler
public class OrderHandler
{
    public Result<Order> Handle(CreateOrder cmd) { }
    public Result<Order> Handle(GetOrder query) { }
    public Result<Order> Handle(UpdateOrder cmd) { }
    public Result Handle(DeleteOrder cmd) { }
}

// ❌ Unrelated operations
public class MixedHandler
{
    public User Handle(GetUser query) { }
    public Order Handle(CreateOrder cmd) { }
    public Email Handle(SendEmail cmd) { }
}
```

### 3. Use Method Injection for Per-Request Dependencies

```csharp
public class OrderHandler
{
    private readonly ILogger _logger; // ✅ Singleton - safe for constructor

    public OrderHandler(ILogger<OrderHandler> logger) => _logger = logger;

    public async Task<Order> HandleAsync(
        CreateOrder command,
        DbContext context,    // ✅ Scoped - use method injection
        ICurrentUser user,     // ✅ Per-request - use method injection
        CancellationToken ct
    )
    {
        // Fresh context and user for each request
    }
}
```

### 4. Keep Handlers Simple and Focused

```csharp
// ✅ Single responsibility
public class CreateOrderHandler
{
    public async Task<Result<Order>> HandleAsync(CreateOrder command)
    {
        // Only handles order creation
    }
}

// ❌ Too many responsibilities
public class OrderHandler
{
    public Result Handle(CreateOrder cmd) { /* ... */ }
    public Result Handle(UpdateInventory cmd) { /* ... */ }
    public Result Handle(SendEmail cmd) { /* ... */ }
    public Result Handle(ProcessPayment cmd) { /* ... */ }
}
```

## Explicit Handler Declaration

In addition to naming conventions, handlers can be explicitly declared using:

1. **Interface** - Classes implementing the `IHandler` marker interface
2. **Attribute** - Classes or methods decorated with `[Handler]`

```csharp
// Discovered via IHandler interface
public class OrderProcessor : IHandler
{
    public Order Handle(CreateOrder command) { }
}

// Discovered via [Handler] attribute on class
[Handler]
public class EmailService
{
    public void Handle(SendEmail command) { }
}

// Discovered via [Handler] attribute on method
public class NotificationService
{
    [Handler]
    public void Process(SendNotification command) { }
}
```

### Disabling Conventional Discovery

If you prefer explicit handler declaration over naming conventions, you can disable conventional discovery entirely:

```xml
<PropertyGroup>
    <MediatorDisableConventionalDiscovery>true</MediatorDisableConventionalDiscovery>
</PropertyGroup>
```

When disabled, only handlers that implement `IHandler` or have the `[Handler]` attribute are discovered. Classes with names ending in `Handler` or `Consumer` will not be automatically discovered.

## Next Steps

- [Result Types](./result-types) - Robust error handling patterns
- [Middleware](./middleware) - Cross-cutting concerns
