# Streaming Handlers

Foundatio Mediator supports streaming handlers that can return `IAsyncEnumerable<T>` for scenarios where you need to process and return data incrementally, such as large datasets, real-time feeds, or progressive data processing.

## Basic Streaming Handler

```csharp
public record GetProductStream(string? CategoryId);

public class ProductStreamHandler
{
    public async IAsyncEnumerable<Product> HandleAsync(
        GetProductStream query,
        IProductRepository repository,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var product in repository.GetProductsAsync(query.CategoryId, cancellationToken))
        {
            yield return product;
        }
    }
}
```

## Consuming Streaming Results

Use `await foreach` to consume a streaming handler's results:

```csharp
await foreach (var product in mediator.InvokeAsync<IAsyncEnumerable<Product>>(
    new GetProductStream("electronics"), cancellationToken))
{
    Console.WriteLine(product.Name);
}
```

You can also use LINQ operators from `System.Linq.Async`:

```csharp
await foreach (var product in mediator.InvokeAsync<IAsyncEnumerable<Product>>(
    new GetProductStream("electronics"), cancellationToken)
    .Where(p => p.Price > 100)
    .Take(50))
{
    await ProcessProductAsync(product);
}
```

## Server-Sent Events (SSE) {#server-sent-events-sse}

For real-time push scenarios, set `Streaming = EndpointStreaming.ServerSentEvents` to use ASP.NET Core's built-in `TypedResults.ServerSentEvents()` (requires .NET 10+):

```csharp
public record GetEventStream;
public record ClientEvent(string EventType, object Data);

public class EventStreamHandler(IMediator mediator)
{
    [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
    public async IAsyncEnumerable<ClientEvent> Handle(
        GetEventStream message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(
            cancellationToken: cancellationToken))
        {
            yield return new ClientEvent(evt.GetType().Name, evt);
        }
    }
}
```

### Client-Side Consumption

SSE endpoints are consumed using the browser's built-in `EventSource` API:

```javascript
const source = new EventSource('/api/events/stream');

source.onmessage = (e) => {
    const data = JSON.parse(e.data);
    console.log('Received:', data);
};
```

`EventSource` automatically handles reconnection — no WebSocket library needed.

### SSE Configuration Options

| Property       | Type                | Description                                                           |
| -------------- | ------------------- | --------------------------------------------------------------------- |
| `Streaming`    | `EndpointStreaming`  | `Default` (JSON array) or `ServerSentEvents` (SSE)                    |
| `SseEventType` | `string?`            | Optional SSE event type passed to `TypedResults.ServerSentEvents()`   |

### When to Use SSE vs Default Streaming

| Scenario               | Recommended          |
| ---------------------- | -------------------- |
| One-time data export   | Default (JSON array) |
| Database query results | Default (JSON array) |
| Real-time event feed   | SSE                  |
| Live notifications     | SSE                  |
| Progress updates       | SSE                  |

For more on endpoint generation, route conventions, and attribute options, see [Endpoint Generation](./endpoints.md).

## Dynamic Subscriptions with SubscribeAsync

Foundatio Mediator supports **dynamic subscriptions** — receive published notifications as an async stream. This is ideal for real-time push scenarios like SSE, where each connected client needs its own live stream of domain events.

### Basic Usage

Call `mediator.SubscribeAsync<T>()` to create a subscription that yields every notification assignable to `T`:

```csharp
await foreach (var evt in mediator.SubscribeAsync<OrderCreated>(cancellationToken: ct))
{
    Console.WriteLine($"Order created: {evt.OrderId}");
}
```

The stream continues until the `CancellationToken` is cancelled. Each subscriber gets its own independent buffered channel — no shared state, no coordination required.

### Interface Subscriptions

Subscribe to an interface or base class to receive all matching types:

```csharp
public interface IDispatchToClient { }
public record OrderCreated(string OrderId) : IDispatchToClient;
public record ProductUpdated(string ProductId) : IDispatchToClient;

// Receives both OrderCreated and ProductUpdated
await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(cancellationToken: ct))
{
    var eventType = evt.GetType().Name; // "OrderCreated" or "ProductUpdated"
}
```

Type matching uses `Type.IsAssignableFrom` and is cached — the check only runs once per unique message type, not per subscriber or per publish.

### Combining SubscribeAsync with SSE

The most common use case is pushing domain events to browser clients via SSE. Combine `SubscribeAsync` with a streaming handler endpoint:

```csharp
public record GetClientEventsStream;
public record ClientEvent(string EventType, object Data);

public class ClientEventStreamHandler(IMediator mediator)
{
    [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
    public async IAsyncEnumerable<ClientEvent> Handle(
        GetClientEventsStream message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(
            cancellationToken: cancellationToken))
        {
            yield return new ClientEvent(evt.GetType().Name, evt);
        }
    }
}
```

When any handler publishes a notification that implements `IDispatchToClient`, every connected SSE client receives it automatically.

### Buffer Configuration

Each subscriber has a bounded buffer (default: 100 items). When full, the oldest unread item is dropped:

```csharp
await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(
    maxCapacity: 10, cancellationToken: ct))
{
    // ...
}
```

### Lifecycle

- **Subscribe:** `SubscribeAsync<T>()` registers the subscription immediately.
- **Receive:** Published notifications matching `T` are written to the subscriber's channel.
- **Unsubscribe:** When the `CancellationToken` is cancelled (e.g., SSE client disconnects), the subscription is automatically removed and the channel is completed.
- **Dispose:** When `HandlerRegistry` is disposed at app shutdown, all active channels are completed so subscribers exit cleanly.

There is zero cost when nobody is subscribed — `PublishAsync` skips the subscription fan-out entirely.
