# Modular Monolith Sample

This sample demonstrates how to structure a modular monolith application using Foundatio.Mediator, with a focus on handling middleware across modules.

## Project Structure

```text
src/
├── Orders.Module/          # Order processing module
│   ├── Handlers/          # Order-related message handlers
│   ├── Messages/          # Order commands, queries, events
│   ├── Middleware/        # Module-specific middleware
│   └── Api/              # Order API endpoints
└── WebApp/               # Main web application
    └── Program.cs        # Application startup
```

## Middleware in Modular Applications

### The Challenge

Middleware must be defined in the **same project** as your handlers because the source generator only has access to the current project's source code at compile-time.

### Solution Options

#### Option 1: Module-Specific Middleware (Current)

Each module defines its own middleware:

```csharp
// Orders.Module/Middleware/ValidationMiddleware.cs
namespace Orders.Module.Middleware;

public static class ValidationMiddleware
{
    public static HandlerResult Before(object message)
    {
        // Validation logic specific to Orders module
    }
}
```

**Pros:**

- Simple and straightforward
- Each module is self-contained
- No file linking required

**Cons:**

- Duplicate middleware code if needed across modules
- Inconsistencies between modules possible

#### Option 2: Shared Middleware via Linked Files

For middleware that should be shared across multiple modules, use linked files:

1. **Create a shared middleware directory:**

   ```text
   src/
   ├── Shared.Middleware/
   │   ├── LoggingMiddleware.cs
   │   ├── ValidationMiddleware.cs
   │   └── AuthorizationMiddleware.cs
   ├── Orders.Module/
   └── Products.Module/
   ```

2. **Link files in each module's `.csproj`:**

   ```xml
   <ItemGroup>
     <!-- Link shared middleware -->
     <Compile Include="..\Shared.Middleware\LoggingMiddleware.cs" Link="Middleware\LoggingMiddleware.cs" />
     <Compile Include="..\Shared.Middleware\ValidationMiddleware.cs" Link="Middleware\ValidationMiddleware.cs" />
   </ItemGroup>
   ```

3. **Declare middleware as `internal`:**

   ```csharp
   // Shared.Middleware/LoggingMiddleware.cs
   namespace Shared.Middleware;

   // Use internal to avoid type conflicts
   internal class LoggingMiddleware
   {
       public void Before(object message) { /* ... */ }
   }
   ```

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
