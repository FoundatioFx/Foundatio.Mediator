![Foundatio](https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio-dark-bg.svg#gh-dark-mode-only "Foundatio")![Foundatio](https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio.svg#gh-light-mode-only "Foundatio")

[![Build status](https://github.com/FoundatioFx/Foundatio.Mediator/workflows/Build/badge.svg)](https://github.com/FoundatioFx/Foundatio.Mediator/actions)
[![NuGet Version](http://img.shields.io/nuget/v/Foundatio.Mediator.svg?style=flat)](https://www.nuget.org/packages/Foundatio.Mediator/)
[![feedz.io](https://img.shields.io/badge/endpoint.svg?url=https%3A%2F%2Ff.feedz.io%2Ffoundatio%2Ffoundatio%2Fshield%2FFoundatio.Mediator%2Flatest)](https://f.feedz.io/foundatio/foundatio/packages/Foundatio.Mediator/latest/download)
[![Discord](https://img.shields.io/discord/715744504891703319)](https://discord.gg/6HxgFCx)

Blazingly fast, convention-based C# mediator powered by source generators and interceptors.

## ✨ Why Choose Foundatio Mediator?

- 🚀 **Near-direct call performance** - Zero runtime reflection, minimal overhead
- ⚡ **Convention-based** - No interfaces or base classes required
- 🔧 **Full DI support** - Microsoft.Extensions.DependencyInjection integration
- 🧩 **Plain handler classes** - Drop in static or instance methods anywhere
- 🎪 **Middleware pipeline** - Before/After/Finally hooks with state passing
- 🎯 **Built-in Result\<T>** - Rich status handling without exceptions
- 🔄 **Tuple returns** - Automatic cascading messages
- 🔒 **Compile-time safety** - Early validation and diagnostics
- 🧪 **Easy testing** - Plain objects, no framework coupling
- 🐛 **Superior debugging** - Short, simple call stacks

## 🚀 Complete Example

### 1. Install & Register

```bash
dotnet add package Foundatio.Mediator
```

```csharp
// Program.cs
services.AddMediator();
```

### 2. Create Messages & Handlers

```csharp
// Messages (records, classes, anything)
public record GetUser(int Id);
public record CreateUser(string Name, string Email);
public record UserCreated(int UserId, string Email);

// Handlers - just plain classes ending with "Handler" or "Consumer"
public class UserHandler
{
    public async Task<Result<User>> HandleAsync(GetUser query, IUserRepository repo)
    {
        var user = await repo.FindAsync(query.Id);
        return user ?? Result.NotFound($"User {query.Id} not found");
    }

    public async Task<(User user, UserCreated evt)> HandleAsync(CreateUser cmd, IUserRepository repo)
    {
        var user = new User { Name = cmd.Name, Email = cmd.Email };
        await repo.AddAsync(user);

        // Return tuple: first element is response, rest are auto-published
        return (user, new UserCreated(user.Id, user.Email));
    }
}

// Event handlers
public class EmailHandler
{
    public async Task HandleAsync(UserCreated evt, IEmailService email)
    {
        await email.SendWelcomeAsync(evt.Email);
    }
}

// Middleware - classes ending with "Middleware"
public class LoggingMiddleware(ILogger<LoggingMiddleware> logger)
{
    public Stopwatch Before(object message) => Stopwatch.StartNew();

    public void Finally(object message, Stopwatch sw, Exception? ex)
    {
        logger.LogInformation("Handled {MessageType} in {Ms}ms",
            message.GetType().Name, sw.ElapsedMilliseconds);
    }
}
```

### 3. Use the Mediator

```csharp
// Query with response
var result = await mediator.InvokeAsync<Result<User>>(new GetUser(123));
if (result.IsSuccess)
    Console.WriteLine($"Found user: {result.Value.Name}");

// Command with automatic event publishing
var user = await mediator.InvokeAsync<User>(new CreateUser("John", "john@example.com"));
// UserCreated event automatically published to EmailHandler

// Publish events to multiple handlers
await mediator.PublishAsync(new UserCreated(user.Id, user.Email));
```

## 📚 Learn More

**👉 [Complete Documentation](https://mediator.foundatio.dev)**

Key topics:

- [Getting Started](https://mediator.foundatio.dev/guide/getting-started.html) - Step-by-step setup
- [Handler Conventions](https://mediator.foundatio.dev/guide/handler-conventions.html) - Discovery rules and patterns
- [Middleware](https://mediator.foundatio.dev/guide/middleware.html) - Pipeline hooks and state management
- [Result Types](https://mediator.foundatio.dev/guide/result-types.html) - Rich status handling
- [Performance](https://mediator.foundatio.dev/guide/performance.html) - Benchmarks vs other libraries
- [Configuration](https://mediator.foundatio.dev/guide/configuration.html) - MSBuild and runtime options

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request. See our [documentation](https://mediator.foundatio.dev) for development guidelines.

## 🔗 Related Projects

[**@martinothamar/Mediator**](https://github.com/martinothamar/Mediator) was the primary source of inspiration for this library, but we wanted to use source interceptors and be conventional rather than requiring interfaces or base classes.

Other mediator and messaging libraries for .NET:

- **[MediatR](https://github.com/jbogard/MediatR)** - Simple, unambitious mediator implementation in .NET with request/response and notification patterns
- **[MassTransit](https://github.com/MassTransit/MassTransit)** - Distributed application framework for .NET with in-process mediator capabilities alongside service bus features

## 📄 License

MIT License
