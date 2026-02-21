# Clean Architecture Sample

This sample demonstrates how to build a modular monolith application using Foundatio.Mediator following **Clean Architecture** principles. It shows domain separation, repository pattern, cross-module communication via mediator, and event-driven loose coupling.

## What This Sample Demonstrates

- **Clean Architecture layers** - Domain, Data, Messages, and Handlers separation
- **Repository Pattern** - Data access abstraction for testability
- **Cross-module communication** - Reports.Module queries other modules via mediator
- **Domain events** - Loose coupling between modules through event handlers
- **Cross-assembly middleware** - Shared middleware across all modules
- **Compile-time code generation** - Zero runtime overhead
- **SvelteKit SPA Frontend** - Modern frontend with Svelte 5 and Tailwind CSS

## Project Structure

```text
src/
├── Common.Module/               # Shared cross-cutting concerns
│   ├── Events/
│   │   └── DomainEvents.cs      # Domain events (OrderCreated, etc.)
│   ├── Handlers/
│   │   ├── AuditEventHandler.cs        # Listens to all domain events
│   │   └── NotificationEventHandler.cs # Sends notifications on events
│   ├── Middleware/
│   │   ├── ObservabilityMiddleware.cs  # Logging + performance monitoring
│   │   └── ValidationMiddleware.cs     # Common validation logic
│   ├── Services/
│   │   ├── IAuditService.cs            # Audit logging abstraction
│   │   └── INotificationService.cs     # Notification abstraction
│   └── ServiceConfiguration.cs
│
├── Products.Module/             # Product catalog bounded context
│   ├── Domain/
│   │   └── Product.cs           # Domain entity
│   ├── Data/
│   │   ├── IProductRepository.cs       # Repository interface
│   │   └── InMemoryProductRepository.cs
│   ├── Handlers/
│   │   └── ProductHandler.cs    # Commands & queries
│   ├── Messages/
│   │   └── ProductMessages.cs   # Commands, queries, events
│   └── ServiceConfiguration.cs
│
├── Orders.Module/               # Order processing bounded context
│   ├── Domain/
│   │   └── Order.cs             # Domain entity
│   ├── Data/
│   │   ├── IOrderRepository.cs
│   │   └── InMemoryOrderRepository.cs
│   ├── Handlers/
│   │   └── OrderHandler.cs
│   ├── Messages/
│   │   └── OrderMessages.cs
│   └── ServiceConfiguration.cs
│
├── Reports.Module/              # Reporting (cross-module aggregation)
│   ├── Handlers/
│   │   └── ReportHandler.cs     # Fetches data from other modules
│   ├── Messages/
│   │   └── ReportMessages.cs    # Queries and DTOs
│   └── ServiceConfiguration.cs
│
├── Api/                         # ASP.NET Core backend (composition root)
│   ├── Program.cs               # Application entry point
│   └── Api.http                 # HTTP request samples
│
└── Web/                         # SvelteKit SPA frontend
    ├── src/
    │   ├── lib/
    │   │   ├── api/             # API clients
    │   │   ├── components/      # Svelte components
    │   │   └── types/           # TypeScript types
    │   └── routes/              # SvelteKit pages
    ├── vite.config.ts           # Vite configuration with API proxy
    └── package.json
```

## Clean Architecture Principles Applied

### 1. Dependency Rule

Dependencies point inward - domain entities have no external dependencies:

```text
┌─────────────────────────────────────────────────────────────┐
│                        Web                                  │
│  (Composition Root - wires everything together)             │
├─────────────────────────────────────────────────────────────┤
│                    Handlers Layer                           │
│  OrderHandler, ProductHandler, ReportHandler                │
├─────────────────────────────────────────────────────────────┤
│                  Data/Infrastructure                        │
│  IOrderRepository, IProductRepository, implementations      │
├─────────────────────────────────────────────────────────────┤
│                     Domain Layer                            │
│  Order, Product, OrderStatus, ProductStatus                 │
└─────────────────────────────────────────────────────────────┘
```

### 2. Repository Pattern

Data access is abstracted behind interfaces:

```csharp
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken);
    Task AddAsync(Order order, CancellationToken cancellationToken);
    Task UpdateAsync(Order order, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}
```

### 3. Cross-Module Communication via Mediator

Reports.Module doesn't reference Orders or Products data access - it queries through the mediator:

```csharp
public class ReportHandler(IMediator mediator, ILogger<ReportHandler> logger)
{
    public async Task<Result<DashboardReport>> HandleAsync(GetDashboardReport query, CancellationToken ct)
    {
        // Fetch data from both modules in parallel via mediator
        var ordersTask = mediator.InvokeAsync(new GetOrders(), ct);
        var productsTask = mediator.InvokeAsync(new GetProducts(), ct);

        await Task.WhenAll(ordersTask.AsTask(), productsTask.AsTask());

        // Aggregate results into report
        return new DashboardReport(...);
    }
}
```

### 4. Domain Events for Loose Coupling

When an order is created, multiple handlers react without the Orders.Module knowing:

```csharp
// In OrderHandler - returns event along with result
return (order, new OrderCreated(order.Id, customerId, amount, DateTime.UtcNow));

// In AuditEventHandler (Common.Module) - listens to event
public async Task HandleAsync(OrderCreated evt, CancellationToken ct)
{
    await auditService.LogAsync(new AuditEntry(...));
}

// In NotificationEventHandler (Common.Module) - also listens
public async Task HandleAsync(OrderCreated evt, CancellationToken ct)
{
    await notificationService.SendAsync(new Notification(...));
}
```

## Running the Sample

### Prerequisites

- .NET 10 SDK
- Node.js 20+ (for the frontend)

### Quick Start

The easiest way to run the sample is using Visual Studio or VS Code:

1. **Install frontend dependencies** (first time only):

   ```bash
   cd samples/CleanArchitectureSample/src/Web
   npm install
   ```

2. **Run the Web project** - this automatically starts both backend and frontend:
   - **Visual Studio**: Set `Api` as the startup project and press F5
   - **VS Code**: Run the "Clean Architecture Sample" launch configuration
   - **CLI**: `dotnet run --project samples/CleanArchitectureSample/src/Api`

The application uses **SPA Proxy** to automatically:

- Start the Vite dev server for the SvelteKit frontend
- Open your browser to `https://localhost:5173`

### URLs

| URL                                    | Description                       |
| -------------------------------------- | --------------------------------- |
| `https://localhost:5173`               | SvelteKit frontend (development)  |
| `https://localhost:58702/api/*`        | Backend API                       |
| `https://localhost:58702/scalar/v1`    | API documentation (Scalar)        |

### Using the HTTP File

Open `src/Api/Api.http` in VS Code or Rider to run sample requests.

### Try the API

**Create a product:**

```bash
curl -X POST https://localhost:58702/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Widget","description":"A great widget","price":29.99,"stockQuantity":50}'
```

**Create an order:**

```bash
curl -X POST https://localhost:58702/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"customer-123","amount":29.99,"description":"Widget purchase"}'
```

**Get dashboard report (aggregates from both modules):**

```bash
curl https://localhost:58702/api/reports
```

**Search across modules:**

```bash
curl "https://localhost:58702/api/reports/search-catalog?searchTerm=widget"
```

### Watch Events in Action

Check the console logs to see domain events flowing:

```text
info: Handling CreateOrder in OrderHandler
info: Completed CreateOrder in OrderHandler (5ms)
dbug: Auditing OrderCreated event for order abc123
dbug: Sending order confirmation notification for order abc123
```

## Frontend Architecture

The SvelteKit frontend demonstrates:

- **Svelte 5** with runes (`$state`, `$derived`) for reactivity
- **Tailwind CSS** for styling
- **TypeScript** for type safety
- **Vite** for fast development with HMR
- **API proxy** during development (requests to `/api/*` are proxied to the backend)

### Frontend Features

- Dashboard with aggregated stats from Orders and Products
- CRUD operations for Orders and Products
- Reports with sales, inventory, and search functionality
- Responsive design with Tailwind CSS

## Key Benefits

### Compile-Time Performance

- Zero runtime reflection
- Middleware woven directly into generated code
- Same performance as hand-written code

### Type Safety

- Full compile-time type checking
- IntelliSense for all messages and handlers
- Catch errors at build time

### Testability

- Repository interfaces enable unit testing without databases
- Handlers can be tested in isolation
- Event handlers can be verified independently

### Loose Coupling

- Modules communicate only through messages
- Adding new event handlers requires no changes to publishing modules
- Each module can evolve independently

## Module Dependencies

```text
Api
  ├── Common.Module (services, middleware)
  ├── Orders.Module (order messages, handlers)
  ├── Products.Module (product messages, handlers)
  ├── Reports.Module (report messages, handlers)
  └── Web (SvelteKit SPA)

Reports.Module
  ├── Common.Module
  ├── Orders.Module (messages only)
  └── Products.Module (messages only)

Orders.Module / Products.Module
  └── Common.Module (middleware, extensions)

Common.Module
  └── Foundatio.Mediator (no domain module dependencies)
```

## Learn More

- [Middleware Documentation](../../docs/guide/middleware.md) - Complete middleware guide
- [Handler Conventions](../../docs/guide/handler-conventions.md) - Handler discovery rules
- [Configuration Options](../../docs/guide/configuration.md) - MSBuild properties
