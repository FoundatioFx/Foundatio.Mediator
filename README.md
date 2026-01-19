![Foundatio](https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio-dark-bg.svg#gh-dark-mode-only "Foundatio")![Foundatio](https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio.svg#gh-light-mode-only "Foundatio")

[![Build status](https://github.com/FoundatioFx/Foundatio.Mediator/workflows/Build/badge.svg)](https://github.com/FoundatioFx/Foundatio.Mediator/actions)
[![NuGet Version](http://img.shields.io/nuget/v/Foundatio.Mediator.svg?style=flat)](https://www.nuget.org/packages/Foundatio.Mediator/)
[![feedz.io](https://img.shields.io/endpoint?url=https%3A%2F%2Ff.feedz.io%2Ffoundatio%2Ffoundatio%2Fshield%2FFoundatio.Mediator%2Flatest)](https://f.feedz.io/foundatio/foundatio/packages/Foundatio.Mediator/latest/download)
[![Discord](https://img.shields.io/discord/715744504891703319)](https://discord.gg/6HxgFCx)

Blazingly fast, convention-based C# mediator powered by source generators and interceptors.

## ✨ Why Choose Foundatio Mediator?

- 🚀 **Near-direct call performance** - Zero runtime reflection, minimal overhead ([see benchmarks](https://mediator.foundatio.dev/guide/performance.html))
- ⚡ **Convention-based** - No interfaces or base classes required
- 🔧 **Full DI support** - Microsoft.Extensions.DependencyInjection integration
- 🧩 **Plain handler classes** - Drop in static or instance methods anywhere
- 🎪 **Middleware pipeline** - Before/After/Finally hooks with state passing
- 🎯 **Built-in Result\<T>** - Rich status handling without exceptions
- 🔄 **Tuple returns** - Automatic cascading messages
- 🌐 **Auto-generated endpoints** - Minimal API endpoints from handlers with zero boilerplate
- 🔒 **Compile-time safety** - Early validation and diagnostics
- 🧪 **Easy testing** - Plain objects, no framework coupling
- 🐛 **Superior debugging** - Short, simple call stacks

### Why Convention-Based?

Traditional mediator libraries force you into rigid interface contracts like `IRequestHandler<TRequest, TResponse>`. This means:

- One handler class per message type
- Fixed method signatures
- Always async (even for simple operations)
- Lots of boilerplate

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

### 4. Auto-Generate API Endpoints (Optional)

Foundatio Mediator can automatically generate ASP.NET Core Minimal API endpoints from your handlers:

```csharp
// Add category to group endpoints
[HandlerCategory("Products", RoutePrefix = "/api/products")]
public class ProductHandler
{
    /// <summary>
    /// Creates a new product in the catalog.
    /// </summary>
    public Task<Result<Product>> HandleAsync(CreateProduct command) { /* ... */ }

    /// <summary>
    /// Gets a product by ID.
    /// </summary>
    public Result<Product> Handle(GetProduct query) { /* ... */ }
}

// Program.cs - map the generated endpoints
app.MapProductsEndpoints();
```

This automatically generates:
- `POST /api/products` → `CreateProduct` handler
- `GET /api/products/{productId}` → `GetProduct` handler
- HTTP method inferred from message name (`Create*` → POST, `Get*` → GET, etc.)
- `Result<T>` status mapped to HTTP status codes
- OpenAPI metadata from XML doc comments

Configure with MSBuild properties:
```xml
<PropertyGroup>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <MediatorProjectName>Products</MediatorProjectName>
    <MediatorEndpointRequireAuth>true</MediatorEndpointRequireAuth>
</PropertyGroup>
```

See [Endpoints Guide](https://mediator.foundatio.dev/guide/endpoints.html) for full documentation.

## 📚 Learn More

**👉 [Complete Documentation](https://mediator.foundatio.dev)**

Key topics:

- [Getting Started](https://mediator.foundatio.dev/guide/getting-started.html) - Step-by-step setup
- [Handler Conventions](https://mediator.foundatio.dev/guide/handler-conventions.html) - Discovery rules and patterns
- [Middleware](https://mediator.foundatio.dev/guide/middleware.html) - Pipeline hooks and state management
- [Result Types](https://mediator.foundatio.dev/guide/result-types.html) - Rich status handling
- [Endpoints](https://mediator.foundatio.dev/guide/endpoints.html) - Auto-generated Minimal API endpoints
- [Performance](https://mediator.foundatio.dev/guide/performance.html) - Benchmarks vs other libraries
- [Configuration](https://mediator.foundatio.dev/guide/configuration.html) - MSBuild and runtime options

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

MIT License
