# Getting Started

Build a completely message-oriented, loosely coupled app that's easy to test — with near-direct-call performance and zero boilerplate. Get up and running in under a minute.

## Quick Start

### 1. Install the package

```bash
dotnet add package Foundatio.Mediator
```

### 2. Define a message and handler

```csharp
// A message is just a record (or class)
public record Ping(string Text);

// Any class ending in "Handler" is discovered automatically
public static class PingHandler
{
    public static string Handle(Ping msg) => $"Pong: {msg.Text}";
}
```

### 3. Wire up DI and call it

::: code-group

```csharp [ASP.NET Core]
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMediator();
var app = builder.Build();

app.MapGet("/ping", (IMediator mediator) =>
    mediator.Invoke<string>(new Ping("Hello")));

app.Run();
```

```csharp [Console / Worker]
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddMediator();
var host = builder.Build();

var mediator = host.Services.GetRequiredService<IMediator>();
var result = mediator.Invoke<string>(new Ping("Hello"));
Console.WriteLine(result); // Pong: Hello
```

:::

That's it. No interfaces, no base classes, no registration — the source generator handles everything at compile time with near-direct-call performance.

::: tip Zero Configuration Required
The library tries to do the right thing by default — discovery, lifetimes, routing, and endpoints all just work. Configuration options exist only as an escape hatch when you need more control. See [Configuration](./configuration) for the full list.
:::

## Async Handlers

Handlers can be async and accept additional parameters resolved from DI:

```csharp
public record GetUser(int Id);

public class UserHandler
{
    public async Task<User> HandleAsync(GetUser query, IUserRepository repo, CancellationToken ct)
    {
        return await repo.GetByIdAsync(query.Id, ct);
    }
}
```

```csharp
var user = await mediator.InvokeAsync<User>(new GetUser(42));
```

The first parameter is always the message. Everything else — services, `CancellationToken` — is injected automatically. See [Handler Conventions](./handler-conventions) for the full set of discovery rules, method names, and signature options.

## Generate API Endpoints

Endpoints are generated automatically for all handlers:

```csharp
public record CreateTodo(string Title);
public record GetTodo(string Id);

public class TodoHandler
{
    public Todo Handle(CreateTodo cmd) => new(Guid.NewGuid().ToString(), cmd.Title);
    public Todo Handle(GetTodo query)  => new(query.Id, "Sample");
}
```

```csharp
// Program.cs
app.MapMediatorEndpoints();
```

This generates:

- `POST /api/todos` → `TodoHandler.Handle(CreateTodo)`
- `GET /api/todos/{id}` → `TodoHandler.Handle(GetTodo)`

<!-- -->

HTTP methods, routes, and parameter binding are all inferred from message names and properties. Routes derive from the message name, are auto-pluralized, and common qualifiers like `All` and `ById` are normalized. See [Endpoints](./endpoints) for route customization, naming conventions, and more.

## Result Types

Return `Result<T>` instead of throwing exceptions for expected failures:

```csharp
public class TodoHandler
{
    public Result<Todo> Handle(GetTodo query, ITodoRepository repo)
    {
        var todo = repo.Find(query.Id);
        if (todo is null)
            return Result.NotFound($"Todo {query.Id} not found");

        return todo; // implicit conversion to Result<Todo>
    }
}
```

When used with generated endpoints, `Result<T>` maps automatically to the correct HTTP status code — `200`, `404`, `400`, `409`, etc. See [Result Types](./result-types) for the full API.

## Events

Publish messages to multiple handlers with `PublishAsync`:

```csharp
public record OrderCreated(string OrderId, DateTime CreatedAt) : INotification;

// Both handlers run when OrderCreated is published
public class EmailHandler
{
    public Task HandleAsync(OrderCreated e, IEmailService email)
        => email.SendAsync($"Order {e.OrderId} confirmed");
}

public class AuditHandler
{
    public void Handle(OrderCreated e, ILogger<AuditHandler> logger)
        => logger.LogInformation("Order {OrderId} created at {Time}", e.OrderId, e.CreatedAt);
}
```

```csharp
await mediator.PublishAsync(new OrderCreated("ORD-001", DateTime.UtcNow));
```

By default, `PublishAsync` waits for all handlers to complete — so you can reliably add event handlers knowing they'll run before the publisher continues. See [Events & Notifications](./events-and-notifications) for the full story on publish strategies, error handling, and dynamic subscriptions.

### Dynamic Subscriptions

Subscribe to published notifications as an async stream — and combine with a streaming handler to get a real-time SSE endpoint in just a few lines:

```csharp
public class EventStreamHandler(IMediator mediator)
{
    [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
    public async IAsyncEnumerable<object> Handle(GetEventStream message, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in mediator.SubscribeAsync<INotification>(ct))
            yield return evt;
    }
}
```

Every notification published anywhere in the app is now pushed to connected clients over SSE.

You can use `SubscribeAsync` to subscribe to any type, including interfaces; any published messages assignable to the type parameter will be delivered. See [Dynamic Subscriptions](./streaming-handlers#dynamic-subscriptions-with-subscribeasync) for the full API.

## Middleware

Add cross-cutting concerns by creating classes ending in `Middleware`:

```csharp
public class LoggingMiddleware
{
    public void Before(object message, ILogger<LoggingMiddleware> logger)
        => logger.LogInformation("→ {MessageType}", message.GetType().Name);

    public void After(object message, ILogger<LoggingMiddleware> logger)
        => logger.LogInformation("← {MessageType}", message.GetType().Name);
}
```

Middleware supports `Before`, `After`, `Finally`, and `ExecuteAsync` hooks with state passing, ordering, and short-circuiting. See [Middleware](./middleware) for the full pipeline.

## Cross-Assembly Handlers

In multi-project solutions, register assemblies so handlers in referenced projects are discovered:

```csharp
builder.Services.AddMediator(c => c
    .AddAssembly<OrderCreated>()    // Orders.Module
    .AddAssembly<CreateProduct>()   // Products.Module
);
```

See [Clean Architecture](./clean-architecture) for a complete modular monolith example.

## Next Steps

| Topic | Description |
| ----- | ----------- |
| [Handler Conventions](./handler-conventions) | All discovery rules, method names, static handlers, explicit attributes |
| [Dependency Injection](./dependency-injection) | Lifetimes, parameter injection, constructor vs method injection |
| [Result Types](./result-types) | `Result<T>` API, status codes, validation errors |
| [Middleware](./middleware) | Pipeline hooks, ordering, state passing, short-circuiting |
| [Endpoints](./endpoints) | Route conventions, OpenAPI, authorization, filters |
| [Configuration](./configuration) | All compile-time and runtime options |
| [Streaming Handlers](./streaming-handlers) | `IAsyncEnumerable<T>` support and dynamic subscriptions |
| [Performance](./performance) | Benchmarks and how interceptors work |
| [Troubleshooting](./troubleshooting) | Common issues and solutions |

::: info LLM-Friendly Docs
For AI assistants, we provide [llms.txt](/llms.txt) and [llms-full.txt](/llms-full.txt) following the [llmstxt.org](https://llmstxt.org/) standard.
:::
