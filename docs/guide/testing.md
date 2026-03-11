# Testing

Because handlers are plain classes with no base types or framework dependencies, testing Foundatio.Mediator applications is straightforward at every level — from isolated unit tests to full HTTP integration tests against generated endpoints.

This guide covers three testing tiers:

1. **Unit testing handlers directly** — call handler methods without the mediator
2. **Integration testing with the mediator** — exercise the full pipeline including DI and middleware
3. **Integration testing generated endpoints** — test auto-generated minimal API endpoints over HTTP

All examples use [xUnit](https://xunit.net/) and follow standard .NET testing conventions.

## Unit Testing Handlers Directly

Handlers are plain classes. You can instantiate them with `new`, call their handle methods directly, and assert the return value — no mediator, no DI container, no framework mocking required.

### Simple Handler

Given a handler:

```csharp
public record GetGreeting(string Name);

public class GreetingHandler
{
    public string Handle(GetGreeting query) => $"Hello, {query.Name}!";
}
```

Test it directly:

```csharp
[Fact]
public void Handle_ReturnsGreeting()
{
    var handler = new GreetingHandler();

    var result = handler.Handle(new GetGreeting("World"));

    Assert.Equal("Hello, World!", result);
}
```

### Handler with Dependencies

When a handler accepts dependencies via constructor injection, pass stubs or mocks directly:

```csharp
public record GetOrder(string OrderId);

public class OrderHandler(IOrderRepository repository)
{
    public async Task<Result<Order>> HandleAsync(
        GetOrder query, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(query.OrderId, cancellationToken);
        return order ?? Result.NotFound($"Order {query.OrderId} not found");
    }
}
```

```csharp
[Fact]
public async Task HandleAsync_WhenOrderExists_ReturnsSuccess()
{
    var expected = new Order("order-1", "customer-1", 99.99m);
    var repo = new FakeOrderRepository(expected);
    var handler = new OrderHandler(repo);

    var result = await handler.HandleAsync(
        new GetOrder("order-1"), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal("order-1", result.Value.Id);
}

[Fact]
public async Task HandleAsync_WhenOrderMissing_ReturnsNotFound()
{
    var repo = new FakeOrderRepository(order: null);
    var handler = new OrderHandler(repo);

    var result = await handler.HandleAsync(
        new GetOrder("missing"), CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(ResultStatus.NotFound, result.Status);
}
```

::: tip DI Method Parameters
Handler methods can also accept additional parameters resolved from DI (like `ILogger<T>`). In unit tests, pass them directly as method arguments:

```csharp

public class OrderHandler
{
    public async Task<Result<Order>> HandleAsync(
        CreateOrder command,
        IOrderRepository repo,     // DI-resolved at runtime
        ILogger<OrderHandler> logger, // DI-resolved at runtime
        CancellationToken ct)
    {
        // ...
    }
}

// In your test:
var result = await handler.HandleAsync(
    new CreateOrder("cust-1", 50m),
    fakeRepo,
    NullLogger<OrderHandler>.Instance,
    CancellationToken.None);
```

:::

### Testing Cascading Events

Handlers that return tuples produce cascading messages. In a unit test, you can assert the returned tuple directly without the mediator publishing anything:

```csharp
[Fact]
public async Task HandleAsync_CreateOrder_ReturnsOrderAndEvent()
{
    var repo = new FakeOrderRepository();
    var handler = new OrderHandler(repo);

    var (result, orderCreated) = await handler.HandleAsync(
        new CreateOrder("cust-1", 100m),
        NullLogger<OrderHandler>.Instance,
        CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.NotNull(orderCreated);
    Assert.Equal("cust-1", orderCreated.CustomerId);
}
```

## Integration Testing with the Mediator

Integration tests exercise the full pipeline: handler discovery, DI resolution, middleware execution, and dispatch. This verifies that everything wires up correctly.

### Basic Setup

Build a real DI container with `AddMediator()` and resolve `IMediator`:

```csharp
[Fact]
public async Task InvokeAsync_ReturnsExpected()
{
    var services = new ServiceCollection();
    services.AddMediator(b => b.AddAssembly<PingHandler>());

    using var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    var result = await mediator.InvokeAsync<string>(new Ping("Hello"));

    Assert.Equal("Hello Pong", result);
}
```

`AddAssembly<T>()` registers all handlers discovered in the assembly containing `T`. For projects using the default `AddMediator()` call, all referenced assemblies with `[assembly: FoundatioModule]` are auto-discovered.

### Testing Events with Multiple Handlers

Use `PublishAsync` to fan out to all registered handlers for a message type:

```csharp
public record OrderCreated(string OrderId);

public class AuditHandler
{
    public static string? LastOrderId { get; set; }
    public void Handle(OrderCreated evt) => LastOrderId = evt.OrderId;
}

public class NotificationHandler
{
    public static bool WasCalled { get; set; }
    public void Handle(OrderCreated evt) => WasCalled = true;
}

[Fact]
public async Task PublishAsync_InvokesAllHandlers()
{
    AuditHandler.LastOrderId = null;
    NotificationHandler.WasCalled = false;

    var services = new ServiceCollection();
    services.AddMediator(b => b.AddAssembly<AuditHandler>());

    using var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    await mediator.PublishAsync(new OrderCreated("order-42"));

    Assert.Equal("order-42", AuditHandler.LastOrderId);
    Assert.True(NotificationHandler.WasCalled);
}
```

### Testing with Middleware

Middleware participates automatically when registered. To test that middleware runs, register it alongside the handler:

```csharp
[Middleware(Order = 0)]
public class TimingMiddleware
{
    public static bool BeforeCalled { get; set; }
    public static bool AfterCalled { get; set; }

    public void Before(object message) => BeforeCalled = true;
    public void After(object message) => AfterCalled = true;
}

[Fact]
public async Task InvokeAsync_ExecutesMiddlewarePipeline()
{
    TimingMiddleware.BeforeCalled = false;
    TimingMiddleware.AfterCalled = false;

    var services = new ServiceCollection();
    services.AddMediator(b => b.AddAssembly<PingHandler>());

    using var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    await mediator.InvokeAsync<string>(new Ping("Test"));

    Assert.True(TimingMiddleware.BeforeCalled);
    Assert.True(TimingMiddleware.AfterCalled);
}
```

### Testing Result Types Through the Mediator

Assert on `Result<T>` properties when handlers return rich results:

```csharp
[Fact]
public async Task InvokeAsync_WhenNotFound_ReturnsNotFoundResult()
{
    var services = new ServiceCollection();
    services.AddSingleton<IOrderRepository>(new FakeOrderRepository(order: null));
    services.AddMediator(b => b.AddAssembly<OrderHandler>());

    using var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    var result = await mediator.InvokeAsync<Result<Order>>(new GetOrder("missing"));

    Assert.False(result.IsSuccess);
    Assert.Equal(ResultStatus.NotFound, result.Status);
}
```

::: tip Scoped Services
If your handlers depend on scoped services, create a scope before resolving the mediator:

```csharp
using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
```

:::

### Verifying Unhandled Messages

The mediator throws `InvalidOperationException` when no handler is registered for a message:

```csharp
[Fact]
public async Task InvokeAsync_WithNoHandler_Throws()
{
    var services = new ServiceCollection();
    services.AddMediator();

    using var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<IMediator>();

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => mediator.InvokeAsync(new UnregisteredMessage()).AsTask());
}
```

## Integration Testing Generated Endpoints

When handlers are decorated with endpoint attributes (or discovered automatically), Foundatio.Mediator generates minimal API endpoints. You can test these over HTTP using ASP.NET Core's `WebApplicationFactory`.

See [Endpoints](./endpoints) for full details on how routes, HTTP methods, and status codes are generated.

### Project Setup

Add the test infrastructure packages to your test project:

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
</ItemGroup>
```

Ensure your API project exposes its entry point for testing. If using top-level statements, add to your API project:

```csharp
// At the bottom of Program.cs (or in a partial class)
public partial class Program { }
```

### Basic Endpoint Test

Given a handler that generates a `GET /api/orders/{orderId}` endpoint:

```csharp
[HandlerEndpointGroup("Orders")]
public class OrderHandler(IOrderRepository repository)
{
    [HandlerAllowAnonymous]
    public async Task<Result<Order>> HandleAsync(
        GetOrder query, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(query.OrderId, cancellationToken);
        return order ?? Result.NotFound($"Order {query.OrderId} not found");
    }
}
```

Test the generated endpoint:

```csharp
public class OrderEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OrderEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebApplicationBuilder(builder =>
        {
            builder.Services.AddSingleton<IOrderRepository>(
                new FakeOrderRepository(
                    new Order("order-1", "cust-1", 99.99m)));
        }).CreateClient();
    }

    [Fact]
    public async Task GetOrder_ReturnsOrder()
    {
        var response = await _client.GetAsync("/api/orders/order-1");

        response.EnsureSuccessStatusCode();
        var order = await response.Content
            .ReadFromJsonAsync<Order>();

        Assert.NotNull(order);
        Assert.Equal("order-1", order.Id);
    }

    [Fact]
    public async Task GetOrder_WhenMissing_Returns404()
    {
        var response = await _client.GetAsync("/api/orders/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

### Testing POST Endpoints

For handlers that create resources, the generated endpoint accepts a JSON body:

```csharp
[Fact]
public async Task CreateOrder_Returns201()
{
    var response = await _client.PostAsJsonAsync("/api/orders",
        new { CustomerId = "cust-1", Amount = 50.00m, Description = "Test" });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    var order = await response.Content.ReadFromJsonAsync<Order>();
    Assert.NotNull(order);
    Assert.Equal("cust-1", order.CustomerId);
}
```

### Testing with Authentication

When endpoints require authorization, configure a test authentication scheme:

```csharp
public class AuthOrderEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthOrderEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAuthenticatedClient(string role = "User")
    {
        return _factory.WithWebApplicationBuilder(builder =>
        {
            builder.Services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    "Test", options => { });
            builder.Services.AddAuthorization();
            builder.Services.Configure<TestAuthOptions>(o => o.Role = role);
        }).CreateClient();
    }

    [Fact]
    public async Task CreateOrder_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders",
            new { CustomerId = "cust-1", Amount = 50m });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_WithUserRole_Succeeds()
    {
        var client = CreateAuthenticatedClient("User");

        var response = await client.PostAsJsonAsync("/api/orders",
            new { CustomerId = "cust-1", Amount = 50m });

        Assert.True(response.IsSuccessStatusCode);
    }
}
```

::: tip Result-to-HTTP Status Mapping
Foundatio.Mediator automatically maps `Result<T>` statuses to HTTP responses:

| Result Status | HTTP Status |
| --- | --- |
| `Success` | 200 OK |
| `Created` | 201 Created |
| `NoContent` | 204 No Content |
| `BadRequest` | 400 Bad Request |
| `Unauthorized` | 401 Unauthorized |
| `Forbidden` | 403 Forbidden |
| `NotFound` | 404 Not Found |
| `Invalid` | 422 Unprocessable Entity |
| `Error` | 500 Internal Server Error |

See [Result Types](./result-types) for details on the `Result<T>` pattern.
:::

### Swapping Services for Testing

Use `WithWebApplicationBuilder` to replace real services with test doubles:

```csharp
var client = factory.WithWebApplicationBuilder(builder =>
{
    builder.Services.AddSingleton<IOrderRepository>(new FakeOrderRepository());
    builder.Services.AddSingleton<IPaymentService>(new FakePaymentService());
}).CreateClient();
```

This lets you control handler behavior without changing any handler code — the same DI injection that powers production code makes test doubles drop in seamlessly.

## Summary

| Testing Tier | What It Tests | Setup Complexity | Speed |
| --- | --- | --- | --- |
| **Unit tests** | Handler logic in isolation | None — `new` up the handler | Fastest |
| **Mediator integration** | DI wiring, middleware, dispatch | `ServiceCollection` + `AddMediator()` | Fast |
| **Endpoint integration** | HTTP routing, serialization, auth | `WebApplicationFactory` | Moderate |

Start with unit tests for business logic, add mediator integration tests for pipeline behavior, and use endpoint tests to verify routing and HTTP semantics. Because handlers are plain classes throughout, each tier builds naturally on the previous one.
