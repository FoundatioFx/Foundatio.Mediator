![Foundatio](https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio-dark-bg.svg#gh-dark-mode-only "Foundatio")![Foundatio](https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio.svg#gh-light-mode-only "Foundatio")

[![Build status](https://github.com/FoundatioFx/Foundatio.Mediator/workflows/Build/badge.svg)](https://github.com/FoundatioFx/Foundatio.Mediator/actions)
[![NuGet Version](http://img.shields.io/nuget/v/Foundatio.Mediator.svg?style=flat)](https://www.nuget.org/packages/Foundatio.Mediator/)
[![feedz.io](https://img.shields.io/endpoint?url=https%3A%2F%2Ff.feedz.io%2Ffoundatio%2Ffoundatio%2Fshield%2FFoundatio.Mediator%2Flatest)](https://f.feedz.io/foundatio/foundatio/packages/Foundatio.Mediator/latest/download)
[![Discord](https://img.shields.io/discord/715744504891703319)](https://discord.gg/6HxgFCx)

Build completely message-oriented, loosely coupled .NET apps that are easy to test — with near-direct-call performance and zero boilerplate. Powered by source generators and interceptors.

## ✨ Why Choose Foundatio Mediator?

- 🚀 **Near-direct call performance** — zero runtime reflection, minimal overhead ([benchmarks](https://mediator.foundatio.dev/guide/performance.html))
- ⚡ **Convention-based** — no interfaces or base classes required
- 🌐 **Auto-generated endpoints** — Minimal API endpoints from handlers, skip the mapping boilerplate
- 🎯 **Built-in Result\<T>** — rich status handling without exceptions, auto-mapped to HTTP status codes
- 🎪 **Middleware pipeline** — Before/After/Finally/Execute hooks with state passing
- 🔄 **Cascading messages** — tuple returns auto-publish events
- 🧩 **Plain handler classes** — static or instance methods, any signature
- 🔧 **Full DI support** — Microsoft.Extensions.DependencyInjection integration
- 🔒 **Compile-time safety** — early validation and diagnostics
- 🧪 **Easy testing** — plain objects, no framework coupling
- 🐛 **Superior debugging** — short, simple call stacks

### Why Convention-Based?

Traditional mediator libraries force you into rigid interface contracts like `IRequestHandler<TRequest, TResponse>`. This means:

- Lots of boilerplate
- Fixed method signatures
- Always async (even for simple operations)
- One handler class per message type

**Foundatio Mediator's conventions give you freedom:**

```csharp
public class OrderHandler
{
    // Sync handler - no async overhead
    public decimal Handle(CalculateTotal query) => query.Items.Sum(i => i.Price);

    // Async with any DI parameters you need
    public async Task<Order> HandleAsync(GetOrder query, IOrderRepo repo, CancellationToken ct)
        => await repo.FindAsync(query.Id, ct);

    // Cascading: first element returned, rest auto-published as events
    public (Order order, OrderCreated evt) Handle(CreateOrder cmd) { /* ... */ }
}

// Static handlers for maximum performance
public static class MathHandler
{
    public static int Handle(Add query) => query.A + query.B;
}
```

> **Prefer explicit interfaces?** Use `IHandler` marker interface or `[Handler]` attributes instead. See [Handler Conventions](https://mediator.foundatio.dev/guide/handler-conventions.html#explicit-handler-declaration).

## 🚀 Quick Start

```bash
dotnet add package Foundatio.Mediator
```

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMediator();
var app = builder.Build();
app.MapMyAppEndpoints(); // generated — see "Auto-Generate API Endpoints" below
app.Run();
```

That's it for setup. Now define messages and handlers:

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

    // Objects or tuples returned from the Before method are available as parameters
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

### 4. Auto-Generate API Endpoints

Skip the boilerplate of manually mapping endpoints to message handlers:

```csharp
// Enable endpoint generation (in any .cs file)
[assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]
```

```csharp
[HandlerCategory("Products", RoutePrefix = "products")]
public class ProductHandler
{
    public Task<Result<Product>> HandleAsync(CreateProduct command) { /* ... */ }
    public Result<Product> Handle(GetProduct query) { /* ... */ }
}
```

This generates:

- `POST /api/products` → `ProductHandler.HandleAsync(CreateProduct)`
- `GET /api/products/{productId}` → `ProductHandler.Handle(GetProduct)`

HTTP methods, routes, and parameter binding are inferred from message names and properties. `Result<T>` maps to the correct HTTP status codes automatically. Pass `logEndpoints: true` to see all mapped routes at startup:

```csharp
app.MapProductsEndpoints(logEndpoints: true);
```

See [Endpoints Guide](https://mediator.foundatio.dev/guide/endpoints.html) for route customization, OpenAPI metadata, authorization, and more.

## 📚 Learn More

**👉 [Complete Documentation](https://mediator.foundatio.dev)**

Key topics:

- [Getting Started](https://mediator.foundatio.dev/guide/getting-started.html) - Step-by-step setup
- [Handler Conventions](https://mediator.foundatio.dev/guide/handler-conventions.html) - Discovery rules and patterns
- [Middleware](https://mediator.foundatio.dev/guide/middleware.html) - Pipeline hooks and state management
- [Result Types](https://mediator.foundatio.dev/guide/result-types.html) - Rich status handling
- [Endpoints](https://mediator.foundatio.dev/guide/endpoints.html) - Auto-generated Minimal API endpoints
- [Performance](https://mediator.foundatio.dev/guide/performance.html) - Benchmarks vs other libraries
- [Configuration](https://mediator.foundatio.dev/guide/configuration.html) - Assembly attribute and runtime options

## 📂 Sample Applications

Explore complete working examples:

- **[Console Sample](samples/ConsoleSample/)** - Simple command-line application demonstrating handlers, middleware, and cascading messages
- **[Clean Architecture Sample](samples/CleanArchitectureSample/)** - Modular monolith showcasing:
  - Clean Architecture layers with domain separation
  - Repository pattern for data access
  - Cross-module communication via mediator
  - Domain events for loose coupling
  - Auto-generated API endpoints
  - Shared middleware across modules

## 🔍 Viewing Generated Code

For debugging purposes, you can inspect the source code generated by Foundatio Mediator. Add this to your `.csproj`:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>

<ItemGroup>
    <Compile Remove="$(CompilerGeneratedFilesOutputPath)/**/*.cs" />
    <Content Include="$(CompilerGeneratedFilesOutputPath)/**/*.cs" />
</ItemGroup>
```

After building, check the `Generated` folder for handler wrappers, DI registrations, and interceptor code. See [Troubleshooting](https://mediator.foundatio.dev/guide/troubleshooting.html) for more details.

## 📦 CI Packages (Feedz)

Want the latest CI build before it hits NuGet? Add the Feedz source (read‑only public) and install the pre-release version:

```bash
dotnet nuget add source https://f.feedz.io/foundatio/foundatio/nuget -n foundatio-feedz
dotnet add package Foundatio.Mediator --prerelease
```

Or add to your `NuGet.config`:

```xml
<configuration>
    <packageSources>
        <add key="foundatio-feedz" value="https://f.feedz.io/foundatio/foundatio/nuget" />
    </packageSources>
    <!-- Optional: limit this source to Foundatio packages -->
    <packageSourceMapping>
        <packageSource key="foundatio-feedz">
            <package pattern="Foundatio.*" />
        </packageSource>
    </packageSourceMapping>
</configuration>
```

CI builds are published with pre-release version tags (e.g. `1.0.0-alpha.12345+sha.abcdef`). Use them to try new features early—avoid in production unless you understand the changes.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request. See our [documentation](https://mediator.foundatio.dev) for development guidelines.

## 🔗 Related Projects

[**@martinothamar/Mediator**](https://github.com/martinothamar/Mediator) was the primary source of inspiration for this library, but we wanted to use source interceptors and be conventional rather than requiring interfaces or base classes.

Other mediator and messaging libraries for .NET:

- **[MediatR](https://github.com/jbogard/MediatR)** - Simple, unambitious mediator implementation in .NET with request/response and notification patterns
- **[MassTransit](https://github.com/MassTransit/MassTransit)** - Distributed application framework for .NET with in-process mediator capabilities alongside service bus features
- **[Immediate.Handlers](https://github.com/ImmediatePlatform/Immediate.Handlers)** - another implementation of the mediator pattern in .NET using source-generation.

## 📄 License

Apache-2.0 License
