# Getting Started

Foundatio.Mediator is a high-performance, convention-based mediator for .NET applications. This guide will walk you through the basic setup and your first handler.

## Installation

Install the Foundatio.Mediator NuGet package:

::: code-group

```bash [.NET CLI]
dotnet add package Foundatio.Mediator
```

```xml [PackageReference]
<PackageReference Include="Foundatio.Mediator" Version="1.0.0" />
```

```powershell [Package Manager]
Install-Package Foundatio.Mediator
```

:::

## Basic Setup

### 1. Register the Mediator

Add the mediator to your dependency injection container:

```csharp
// Program.cs (Minimal API)
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator();

var app = builder.Build();
```

```csharp
// Program.cs (Generic Host)
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddMediator();
    })
    .Build();
```

```csharp
// Startup.cs (Traditional ASP.NET Core)
public void ConfigureServices(IServiceCollection services)
{
    services.AddMediator();
    // ... other services
}
```

### 2. Create Your First Message

Define a simple message record:

```csharp
public record Ping(string Text);
```

### 3. Create a Handler

Create a handler class following the naming conventions:

```csharp
public static class PingHandler
{
    public static string Handle(Ping msg)
    {
        return $"Pong: {msg.Text}";
    }
}
```

### 4. Use the Mediator

Inject and use the mediator in your application:

```csharp
public class MyService
{
    private readonly IMediator _mediator;

    public MyService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public void DoSomething()
    {
        // Sync call - works when all handlers and middleware are sync
        var result = _mediator.Invoke<string>(new Ping("Hello"));
        Console.WriteLine(result); // Output: "Pong: Hello"
    }

    public async Task DoSomethingAsync()
    {
        // Async call - works with both sync and async handlers
        var result = await _mediator.InvokeAsync<string>(new Ping("Hello"));
        Console.WriteLine(result); // Output: "Pong: Hello"
    }
}
```

## Handler Conventions

Foundatio.Mediator uses simple naming conventions to discover handlers automatically:

### Class Names
Handler classes must end with:
- `Handler`
- `Consumer`

### Method Names
Valid handler method names:
- `Handle` / `HandleAsync`
- `Handles` / `HandlesAsync`
- `Consume` / `ConsumeAsync`
- `Consumes` / `ConsumesAsync`

### Method Signatures
- **First parameter**: The message object (required)
- **Remaining parameters**: Injected via dependency injection
- **Return type**: Any type including `void`, `Task`, `Task<T>`

## Examples

### Synchronous Handler
```csharp
public record GetGreeting(string Name);

public static class GreetingHandler
{
    public static string Handle(GetGreeting query)
    {
        return $"Hello, {query.Name}!";
    }
}

// Usage
var greeting = mediator.Invoke<string>(new GetGreeting("World"));
```

### Asynchronous Handler
```csharp
public record SendEmail(string To, string Subject, string Body);

public class EmailHandler
{
    public async Task HandleAsync(SendEmail command)
    {
        // Simulate sending email
        await Task.Delay(100);
        Console.WriteLine($"Email sent to {command.To}");
    }
}

// Usage
await mediator.InvokeAsync(new SendEmail("user@example.com", "Hello", "World"));
```

### Handler with Dependency Injection
```csharp
public class UserHandler
{
    private readonly IUserRepository _repository;
    private readonly ILogger<UserHandler> _logger;

    public UserHandler(IUserRepository repository, ILogger<UserHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<User> HandleAsync(GetUser query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting user {UserId}", query.Id);
        return await _repository.GetByIdAsync(query.Id, cancellationToken);
    }
}
```

## Next Steps

Now that you have the basics working, explore more advanced features:

- [Handler Conventions](./handler-conventions) - Learn all the discovery rules
- [Result Types](./result-types) - Using Result&lt;T&gt; for robust error handling
- [Middleware](./middleware) - Adding cross-cutting concerns
- [Examples](../examples/simple-handlers) - See practical examples

## Common Issues

### Handler Not Found
If you get a "handler not found" error:
1. Ensure your class name ends with `Handler` or `Consumer`
2. Ensure your method name follows the naming conventions
3. Ensure the first parameter matches your message type exactly

### Performance Considerations

- Handlers are singleton by default (constructor dependencies resolved once)
- Use method parameters for per-request dependencies
