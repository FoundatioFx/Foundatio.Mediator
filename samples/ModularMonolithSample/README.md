# Modular Monolith Sample

This sample demonstrates how to build a modular monolith application using Foundatio.Mediator with **cross-assembly middleware discovery**. It shows how shared middleware defined in `Common.Module` is automatically discovered and applied to handlers in `Products.Module` and `Orders.Module`.

## What This Sample Demonstrates

- ✅ **Cross-assembly middleware** - Share middleware across multiple modules
- ✅ **Module-specific middleware** - Each module can have its own middleware
- ✅ **Compile-time code generation** - Zero runtime overhead
- ✅ **Clean architecture** - Clear separation of concerns between modules
- ✅ **Minimal API integration** - ASP.NET Core endpoints with mediator pattern

## Project Structure

```text
src/
├── Common.Module/               # Shared cross-cutting concerns
│   ├── Middleware/
│   │   ├── LoggingMiddleware.cs      # Logs all message handling
│   │   ├── PerformanceMiddleware.cs  # Tracks execution time
│   │   └── ValidationMiddleware.cs   # Common validation logic
│   ├── Extensions/              # Result type extensions
│   └── ServiceConfiguration.cs  # DI registration
│
├── Products.Module/             # Product catalog domain
│   ├── Api/
│   │   └── ProductApi.cs        # Minimal API endpoints
│   ├── Handlers/
│   │   └── ProductHandlers.cs   # Message handlers
│   ├── Messages/
│   │   └── ProductMessages.cs   # Commands, queries, events
│   ├── Middleware/
│   │   └── ProductsModuleMiddleware.cs  # Product-specific middleware
│   └── ServiceConfiguration.cs
│
├── Orders.Module/               # Order processing domain
│   ├── Api/
│   │   └── OrdersApi.cs
│   ├── Handlers/
│   │   └── OrderHandlers.cs
│   ├── Messages/
│   │   └── OrderMessages.cs
│   ├── Middleware/
│   │   └── OrdersModuleMiddleware.cs
│   └── ServiceConfiguration.cs
│
└── WebApp/                      # Main application host
    └── Program.cs               # Application startup & module registration
```

## How Cross-Assembly Middleware Works

### 1. Common.Module Setup

**Common.Module.csproj** - References Foundatio.Mediator as an analyzer:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\..\src\Foundatio.Mediator.Abstractions\Foundatio.Mediator.Abstractions.csproj" />
  <ProjectReference Include="..\..\..\..\src\Foundatio.Mediator\Foundatio.Mediator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

**LoggingMiddleware.cs** - Uses `[Middleware]` attribute to set order:

```csharp
[Middleware(Order = 1)]  // Runs first
public static class LoggingMiddleware
{
    public static void Before(object message, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        logger.LogInformation("Handling {MessageType} in {HandlerType}",
            message.GetType().Name, info.HandlerType.Name);
    }

    public static void After(object message, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        logger.LogInformation("Completed {MessageType} in {HandlerType}",
            message.GetType().Name, info.HandlerType.Name);
    }
}
```

**PerformanceMiddleware.cs** - Discovered by naming convention (ends with `Middleware`):

```csharp
public class PerformanceMiddleware
{
    public Stopwatch Before(object message, HandlerExecutionInfo info)
    {
        return Stopwatch.StartNew();
    }

    public void Finally(object message, Stopwatch stopwatch, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        stopwatch.Stop();
        logger.LogDebug("{HandlerType} handled {MessageType} in {ElapsedMs}ms",
            info.HandlerType.Name, message.GetType().Name, stopwatch.ElapsedMilliseconds);
    }
}
```

### 2. Products.Module Setup

**Products.Module.csproj** - References both Common.Module and Foundatio.Mediator:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\..\src\Foundatio.Mediator.Abstractions\Foundatio.Mediator.Abstractions.csproj" />
  <ProjectReference Include="..\..\..\..\src\Foundatio.Mediator\Foundatio.Mediator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <ProjectReference Include="..\Common.Module\Common.Module.csproj" />
</ItemGroup>
```

**ProductsModuleMiddleware.cs** - Module-specific middleware:

```csharp
[Middleware(Order = 3)]
public static class ProductsModuleMiddleware
{
    public static void Before(object message, ILogger<IMediator> logger)
    {
        logger.LogInformation("ProductsModuleMiddleware Before handling {MessageType}",
            message.GetType().Name);
    }
}
```

### 3. Source Generator Magic

At compile time, when building Products.Module:

1. The Foundatio.Mediator source generator runs
2. It scans **Common.Module's metadata** for middleware types
3. It discovers `LoggingMiddleware` and `PerformanceMiddleware`
4. It discovers local `ProductsModuleMiddleware`
5. It generates handler wrappers that call all middleware in order

**Generated handler code** (simplified):

```csharp
// This is generated automatically - you never write this!
public static async Task<Result<Product>> CreateProduct_Handler(CreateProduct message, ...)
{
    // Common.Module middleware (Order = 1)
    LoggingMiddleware.Before(message, info, logger);

    // Products.Module middleware (Order = 3)
    ProductsModuleMiddleware.Before(message, logger);

    // Common.Module middleware (discovered by convention)
    var stopwatch = performanceMiddleware.Before(message, info);

    try
    {
        // Actual handler execution
        var result = await productHandler.HandleAsync(message, ...);

        LoggingMiddleware.After(message, info, logger);
        return result;
    }
    finally
    {
        performanceMiddleware.Finally(message, stopwatch, info, logger);
    }
}
```

## Middleware Execution Order

For a `CreateProduct` command in Products.Module:

```text
┌─ Before Phase (top to bottom) ─────────────────────┐
│ 1. LoggingMiddleware.Before        [Common.Module]  │
│ 2. ProductsModuleMiddleware.Before [Products.Module]│
│ 3. PerformanceMiddleware.Before    [Common.Module]  │
├─────────────────────────────────────────────────────┤
│ 4. ProductHandler.HandleAsync                       │
├─ After Phase (bottom to top) ──────────────────────┤
│ 5. LoggingMiddleware.After         [Common.Module]  │
├─ Finally Phase (always runs, bottom to top) ────── │
│ 6. PerformanceMiddleware.Finally   [Common.Module]  │
└─────────────────────────────────────────────────────┘
```

## Middleware Discovery Rules

The source generator finds middleware using:

1. **Naming Convention**: Classes ending with `Middleware`
   - Example: `LoggingMiddleware`, `PerformanceMiddleware`
2. **Attribute**: Classes marked with `[Middleware]` or `[Middleware(Order = n)]`

### Setting Execution Order

Use `[Middleware(Order = n)]` to control when middleware runs:

```csharp
[Middleware(Order = 1)]   // Runs first in Before, last in After/Finally
public class LoggingMiddleware { }

[Middleware(Order = 3)]   // Runs after Order 1, before higher numbers
public class ProductsModuleMiddleware { }

// No order specified - runs after ordered middleware
public class PerformanceMiddleware { }
```

## Running the Sample

### Build and Run

```bash
cd samples/ModularMonolithSample
dotnet build
dotnet run --project src/WebApp
```

Visit **https://localhost:5001/swagger** to explore the API.

### Try the API

**Create a product:**

```bash
curl -X POST https://localhost:5001/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Widget","description":"A great widget","price":29.99}'
```

**Get all products:**

```bash
curl https://localhost:5001/api/products
```

**Get a specific product:**

```bash
curl https://localhost:5001/api/products/{productId}
```

**Update a product:**

```bash
curl -X PUT https://localhost:5001/api/products/{productId} \
  -H "Content-Type: application/json" \
  -d '{"name":"Super Widget","description":"An even better widget","price":39.99}'
```

### Watch Middleware in Action

Check the console logs to see middleware execution:

```text
info: Foundatio.Mediator.IMediator[0]
      Handling CreateProduct in ProductHandler
info: Foundatio.Mediator.IMediator[0]
      ProductsModuleMiddleware Before handling CreateProduct
info: Foundatio.Mediator.IMediator[0]
      ProductHandler handled CreateProduct in 2ms
info: Foundatio.Mediator.IMediator[0]
      Completed CreateProduct in ProductHandler
```

## Key Benefits

### ✅ Compile-Time Performance

- Zero runtime reflection or discovery overhead
- Middleware calls are directly woven into generated code
- Same performance as hand-written code

### ✅ Type Safety

- Full compile-time type checking
- IntelliSense works perfectly
- Catch errors at build time, not runtime

### ✅ Modular Architecture

```text
Common.Module      → Cross-cutting concerns (logging, performance, validation)
Products.Module    → Product domain logic + product-specific middleware
Orders.Module      → Order domain logic + order-specific middleware
WebApp            → Composition root - wires everything together
```

## Key Takeaways

1. **Cross-assembly middleware works automatically** - Just reference projects that have Foundatio.Mediator
2. **Both projects need the source generator** - Common.Module and Products.Module both reference Foundatio.Mediator as an analyzer
3. **Middleware is compile-time** - All wiring happens during build, not at runtime
4. **Order matters** - Use `[Middleware(Order = n)]` for precise control
5. **Mix and match** - Combine shared middleware from Common.Module with module-specific middleware

## Learn More

- [Middleware Documentation](../../docs/guide/middleware.md) - Complete middleware guide
- [Handler Conventions](../../docs/guide/handler-conventions.md) - Handler discovery rules
- [Configuration Options](../../docs/guide/configuration.md) - MSBuild properties and settings
