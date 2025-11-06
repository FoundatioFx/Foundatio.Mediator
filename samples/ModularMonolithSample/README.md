# Modular Monolith Sample

This sample demonstrates how to structure a modular monolith application using Foundatio.Mediator with **cross-assembly middleware discovery**. Shared middleware defined in `Common.Module` is automatically discovered and applied to handlers in other modules.

## Project Structure

```text
src/
├── Common.Module/          # Shared middleware (cross-assembly)
│   └── Middleware/        # LoggingMiddleware, PerformanceMiddleware
├── Products.Module/        # Product catalog module
│   ├── Handlers/          # Product-related message handlers
│   ├── Messages/          # Product commands, queries, events
│   ├── Middleware/        # Product-specific validation middleware
│   ├── Services/          # Product repository
│   └── Api/              # Product API endpoints
├── Orders.Module/          # Order processing module
│   ├── Handlers/          # Order-related message handlers
│   ├── Messages/          # Order commands, queries, events
│   ├── Middleware/        # Module-specific middleware
│   └── Api/              # Order API endpoints
└── WebApp/               # Main web application
    └── Program.cs        # Application startup
```

## Cross-Assembly Middleware

### How It Works

1. **Common.Module** has the Foundatio.Mediator source generator referenced
2. Middleware classes in Common.Module follow the naming convention (ending with `Middleware`) or are marked with `[Middleware]` attribute
3. **Products.Module** references Common.Module and also has the Foundatio.Mediator source generator referenced
4. At compile time, the source generator:
   - Scans Common.Module metadata for middleware types
   - Discovers `LoggingMiddleware` and `PerformanceMiddleware`
   - Generates handler code that calls both cross-assembly and local middleware

### Middleware Execution Order

For a `CreateProduct` command in Products.Module:

```text
1. LoggingMiddleware.Before      (Order = 1,  from Common.Module)
2. ValidationMiddleware.Before   (Order = 5,  from Products.Module)
3. PerformanceMiddleware.Before  (Order = 10, from Common.Module)
4. ProductHandler.Handle         (executes)
5. PerformanceMiddleware.Finally (measures execution time)
6. LoggingMiddleware.After       (logs completion)
7. LoggingMiddleware.Finally     (logs errors if any)
```

### Example: Common.Module Middleware

```csharp
// Common.Module/Common.Module.csproj
<ItemGroup>
  <!-- Include Foundatio.Mediator source generator -->
  <PackageReference Include="Foundatio.Mediator" />
</ItemGroup>

// Common.Module/Middleware/LoggingMiddleware.cs
[Middleware(Order = 1)]  // Runs first - Order can be set via attribute
public static class LoggingMiddleware
{
    public static void Before(object message, HandlerExecutionInfo info, ILogger logger)
    {
        logger.LogInformation("Handling {MessageType}", message.GetType().Name);
    }
}
```

### Example: Products.Module (Consumes Cross-Assembly Middleware)

```csharp
// Products.Module/Products.Module.csproj
<ItemGroup>
  <!-- Reference Common.Module to get shared middleware -->
  <ProjectReference Include="..\Common.Module\Common.Module.csproj" />
  <!-- Include Foundatio.Mediator source generator -->
  <PackageReference Include="Foundatio.Mediator" />
</ItemGroup>

// Products.Module/Middleware/ValidationMiddleware.cs
// This middleware follows naming convention (ends with Middleware)
// so no attribute is required, but you can use [Middleware(Order = 5)] to set order
public static class ValidationMiddleware
{
    public static HandlerResult Before(CreateProduct command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return HandlerResult.ShortCircuit(Result.ValidationError(...));

        return HandlerResult.Continue();
    }
}
```

## Benefits

### ✅ No File Linking Required

- Simply reference the middleware assembly (that has the source generator referenced)
- No `.csproj` file linking or `<Compile Include="...">` needed
- Clean project structure
- Middleware can be in any referenced project with Foundatio.Mediator package

### ✅ Compile-Time Performance

- Zero runtime reflection or discovery overhead
- Middleware calls are baked into generated handler code
- Same performance as hand-written code

### ✅ Type Safety

- Full compile-time type checking
- IntelliSense support for middleware dependencies
- Errors caught during build, not at runtime

### ✅ Clear Separation of Concerns

```text
Common.Module     → Cross-cutting middleware (logging, performance, auth)
Products.Module   → Product-specific validation and business logic
Orders.Module     → Order-specific validation and business logic
```

## Running the Sample

```bash
cd samples/ModularMonolithSample
dotnet build
dotnet run --project src/WebApp
```

Visit `https://localhost:5001/swagger` to explore the API.

### Try It Out

```bash
# Create a product (will trigger all middleware)
curl -X POST https://localhost:5001/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Widget","description":"A great widget","price":29.99}'

# Get a product
curl https://localhost:5001/api/products/{id}

# Update a product
curl -X PUT https://localhost:5001/api/products/{id} \
  -H "Content-Type: application/json" \
  -d '{"name":"Super Widget","description":"An even better widget","price":39.99}'
```

Watch the logs to see middleware execution:

```text
info: Common.Module.Middleware.LoggingMiddleware[0]
      Handling CreateProduct in ProductHandler
info: Products.Module.Middleware.ValidationMiddleware[0]
      Validating CreateProduct
info: Common.Module.Middleware.PerformanceMiddleware[0]
      ProductHandler handled CreateProduct in 15ms
info: Common.Module.Middleware.LoggingMiddleware[0]
      Completed CreateProduct in ProductHandler
```

## Middleware Discovery

Middleware is discovered automatically by the Foundatio.Mediator source generator in any referenced project that includes the generator. The generator finds middleware using:

1. **Naming Convention**: Classes ending with `Middleware` (e.g., `LoggingMiddleware`, `ValidationMiddleware`)
2. **Attribute**: Classes or methods marked with `[Middleware]` attribute

### Setting Middleware Order

You can control the execution order of middleware using the `[Middleware(Order = n)]` attribute:

```csharp
[Middleware(Order = 1)]  // Runs first in Before, last in After/Finally
public class LoggingMiddleware { }

[Middleware(Order = 10)] // Runs later in Before, earlier in After/Finally
public class PerformanceMiddleware { }
```

Lower order values run first during the `Before` phase, and last during the `After`/`Finally` phases (reverse order for proper nesting).

**Pros:**

- Single source of truth for shared middleware
- Consistent behavior across modules
- Compile-time performance (middleware baked into handlers)

**Cons:**

- Requires `.csproj` configuration
- Same source file compiled into multiple assemblies

## Running the Sample

```bash
# From the samples/ModularMonolithSample directory
dotnet run --project src/WebApp
```

## Key Takeaways

1. **Middleware is compile-time**: It's woven into handler wrappers during build
2. **Same-project requirement**: Middleware must be in the same project as handlers
3. **Use linked files**: Share middleware source across projects without duplicating code
4. **Use `internal` visibility**: Prevent type conflicts when linking files
5. **Module autonomy**: Each module can have its own middleware + shared middleware

## See Also

- [Middleware Documentation](../../docs/guide/middleware.md) - Complete middleware guide
- [Handler Conventions](../../docs/guide/handler-conventions.md) - Handler discovery rules
