# Streaming Handlers

Foundatio Mediator supports streaming handlers that can return `IAsyncEnumerable<T>` for scenarios where you need to process and return data incrementally, such as large datasets, real-time feeds, or progressive data processing.

## Basic Streaming Handler

```csharp
public class ProductStreamHandler
{
    public static async IAsyncEnumerable<Product> Handle(
        GetProductsStreamQuery query,
        IProductRepository repository,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var product in repository.GetProductsAsync(query.CategoryId, cancellationToken))
        {
            // Process each product before yielding
            product.CalculateDiscountPrice();
            yield return product;
        }
    }
}
```

## Consuming Streaming Results

### Basic Consumption

```csharp
public class ProductController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("stream")]
    public async IAsyncEnumerable<Product> GetProductsStream(
        [FromQuery] string categoryId,
        CancellationToken cancellationToken)
    {
        var query = new GetProductsStreamQuery(categoryId);

        await foreach (var product in _mediator.Invoke<IAsyncEnumerable<Product>>(query, cancellationToken))
        {
            yield return product;
        }
    }
}
```

### Processing with LINQ

```csharp
public async Task ProcessProductsAsync()
{
    var query = new GetProductsStreamQuery("electronics");

    await foreach (var product in _mediator.Invoke<IAsyncEnumerable<Product>>(query)
        .Where(p => p.Price > 100)
        .Take(50))
    {
        await ProcessProductAsync(product);
    }
}
```

## Real-World Examples

### Large Dataset Processing

```csharp
public record GetOrdersStreamQuery(DateTime StartDate, DateTime EndDate, int BatchSize = 100);

public class OrderStreamHandler
{
    public static async IAsyncEnumerable<OrderSummary> Handle(
        GetOrdersStreamQuery query,
        IOrderRepository repository,
        ILogger<OrderStreamHandler> logger,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting order stream for period {Start} to {End}",
            query.StartDate, query.EndDate);

        var totalProcessed = 0;

        await foreach (var batch in repository.GetOrderBatchesAsync(
            query.StartDate, query.EndDate, query.BatchSize, cancellationToken))
        {
            foreach (var order in batch)
            {
                var summary = new OrderSummary
                {
                    Id = order.Id,
                    CustomerEmail = order.CustomerEmail,
                    Total = order.Total,
                    Status = order.Status,
                    ProcessedAt = DateTime.UtcNow
                };

                totalProcessed++;

                if (totalProcessed % 1000 == 0)
                {
                    logger.LogInformation("Processed {Count} orders", totalProcessed);
                }

                yield return summary;
            }
        }

        logger.LogInformation("Completed order stream. Total processed: {Total}", totalProcessed);
    }
}
```

### Real-Time Data Feed

```csharp
public record GetLiveStockPricesQuery(string[] Symbols);

public class StockPriceStreamHandler
{
    public static async IAsyncEnumerable<StockPrice> Handle(
        GetLiveStockPricesQuery query,
        IStockPriceService stockService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Subscribe to real-time stock price updates
        await foreach (var priceUpdate in stockService.SubscribeToSymbols(query.Symbols, cancellationToken))
        {
            var stockPrice = new StockPrice
            {
                Symbol = priceUpdate.Symbol,
                Price = priceUpdate.CurrentPrice,
                Change = priceUpdate.PriceChange,
                Volume = priceUpdate.Volume,
                Timestamp = priceUpdate.Timestamp
            };

            yield return stockPrice;
        }
    }
}
```

### File Processing

```csharp
public record ProcessCsvFileQuery(string FilePath);

public class CsvProcessorHandler
{
    public static async IAsyncEnumerable<CustomerRecord> Handle(
        ProcessCsvFileQuery query,
        ICsvParser csvParser,
        IValidator<CustomerRecord> validator,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var line in csvParser.ReadLinesAsync(query.FilePath, cancellationToken))
        {
            if (csvParser.TryParseCustomer(line, out var customer))
            {
                var validationResult = await validator.ValidateAsync(customer, cancellationToken);

                if (validationResult.IsValid)
                {
                    yield return customer;
                }
                else
                {
                    // Could yield error records or log validation failures
                    await LogValidationErrorAsync(line, validationResult.Errors);
                }
            }
        }
    }
}
```

## Advanced Streaming Patterns

### Streaming with Transformation

```csharp
public class DataTransformStreamHandler
{
    public static async IAsyncEnumerable<ProcessedData> Handle(
        TransformDataStreamQuery query,
        IDataSource dataSource,
        ITransformationService transformer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var rawData in dataSource.GetDataStreamAsync(query.Filter, cancellationToken))
        {
            // Apply transformations
            var transformed = await transformer.TransformAsync(rawData, cancellationToken);

            // Apply business rules
            if (transformer.MeetsBusinessCriteria(transformed))
            {
                yield return transformed;
            }
        }
    }
}
```

### Streaming with Aggregation

```csharp
public class SalesReportStreamHandler
{
    public static async IAsyncEnumerable<SalesMetrics> Handle(
        GenerateSalesReportQuery query,
        ISalesRepository repository,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var currentPeriod = query.StartDate;

        while (currentPeriod <= query.EndDate)
        {
            var endPeriod = currentPeriod.AddDays(query.PeriodDays);

            var sales = await repository.GetSalesForPeriodAsync(currentPeriod, endPeriod, cancellationToken);

            var metrics = new SalesMetrics
            {
                Period = currentPeriod,
                TotalSales = sales.Sum(s => s.Amount),
                OrderCount = sales.Count(),
                AverageOrderValue = sales.Average(s => s.Amount),
                TopProduct = sales.GroupBy(s => s.ProductId)
                    .OrderByDescending(g => g.Sum(s => s.Amount))
                    .First().Key
            };

            yield return metrics;
            currentPeriod = endPeriod;
        }
    }
}
```

### Conditional Streaming

```csharp
public class ConditionalStreamHandler
{
    public static async IAsyncEnumerable<ProcessedItem> Handle(
        ProcessItemsQuery query,
        IItemRepository repository,
        IBusinessRuleEngine ruleEngine,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in repository.GetItemsAsync(query.Filter, cancellationToken))
        {
            // Apply business rules to determine if item should be processed
            var ruleResult = await ruleEngine.EvaluateAsync(item, cancellationToken);

            if (ruleResult.ShouldProcess)
            {
                var processed = new ProcessedItem
                {
                    Id = item.Id,
                    Data = item.Data,
                    ProcessingRules = ruleResult.AppliedRules,
                    ProcessedAt = DateTime.UtcNow
                };

                // Additional conditional logic
                if (ruleResult.RequiresEnrichment)
                {
                    processed = await EnrichItemAsync(processed, cancellationToken);
                }

                yield return processed;
            }
        }
    }
}
```

## Error Handling in Streams

### Graceful Error Recovery

```csharp
public class RobustStreamHandler
{
    public static async IAsyncEnumerable<Result<ProcessedData>> Handle(
        ProcessDataStreamQuery query,
        IDataProcessor processor,
        ILogger<RobustStreamHandler> logger,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in GetDataStreamAsync(query, cancellationToken))
        {
            Result<ProcessedData> result;

            try
            {
                var processed = await processor.ProcessAsync(item, cancellationToken);
                result = Result<ProcessedData>.Success(processed);
            }
            catch (ProcessingException ex)
            {
                logger.LogWarning(ex, "Failed to process item {ItemId}", item.Id);
                result = Result<ProcessedData>.Error($"Processing failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error processing item {ItemId}", item.Id);
                result = Result<ProcessedData>.Error("Unexpected processing error");
            }

            yield return result;
        }
    }
}
```

### Circuit Breaker Pattern

```csharp
public class CircuitBreakerStreamHandler
{
    private static int _consecutiveFailures = 0;
    private const int MaxFailures = 5;

    public static async IAsyncEnumerable<ProcessedData> Handle(
        ProcessStreamQuery query,
        IExternalService externalService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in GetItemsAsync(query, cancellationToken))
        {
            if (_consecutiveFailures >= MaxFailures)
            {
                throw new InvalidOperationException("Circuit breaker is open due to consecutive failures");
            }

            try
            {
                var result = await externalService.ProcessAsync(item, cancellationToken);
                _consecutiveFailures = 0; // Reset on success
                yield return result;
            }
            catch (Exception)
            {
                _consecutiveFailures++;
                throw;
            }
        }
    }
}
```

## Performance Considerations

### Buffering for Better Performance

```csharp
public class BufferedStreamHandler
{
    public static async IAsyncEnumerable<ProcessedBatch> Handle(
        ProcessBatchStreamQuery query,
        IDataSource dataSource,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new List<DataItem>(query.BatchSize);

        await foreach (var item in dataSource.GetDataAsync(cancellationToken))
        {
            buffer.Add(item);

            if (buffer.Count >= query.BatchSize)
            {
                var batch = await ProcessBatchAsync(buffer, cancellationToken);
                yield return batch;
                buffer.Clear();
            }
        }

        // Process remaining items
        if (buffer.Count > 0)
        {
            var finalBatch = await ProcessBatchAsync(buffer, cancellationToken);
            yield return finalBatch;
        }
    }
}
```

### Memory-Efficient Processing

```csharp
public class MemoryEfficientHandler
{
    public static async IAsyncEnumerable<string> Handle(
        ProcessLargeFileQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var fileStream = new FileStream(query.FilePath, FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(fileStream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            // Process line without loading entire file into memory
            var processed = ProcessLine(line);

            if (!string.IsNullOrEmpty(processed))
                yield return processed;
        }
    }
}
```

## Dynamic Subscriptions with SubscribeAsync

Foundatio Mediator supports **dynamic subscriptions** — receive published notifications as an async stream. This is ideal for real-time push scenarios like Server-Sent Events (SSE), where each connected client needs its own live stream of domain events.

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
    // evt is typed as IDispatchToClient, but the actual type is preserved
    var eventType = evt.GetType().Name; // "OrderCreated" or "ProductUpdated"
}
```

Type matching uses `Type.IsAssignableFrom` and is cached — the check only runs once per unique message type, not per subscriber or per publish.

### SSE Streaming Example

The most common use case is pushing domain events to browser clients via SSE. Combine `SubscribeAsync` with a streaming handler endpoint:

```csharp
public record SubscribeToClientEvents;
public record ClientEvent(string EventType, object Data);

[Handler]
public class ClientEventStreamHandler(IMediator mediator)
{
    [HandlerEndpoint(
        Route = "/events/stream",
        Streaming = EndpointStreaming.ServerSentEvents,
        SseEventType = "event")]
    [HandlerAllowAnonymous]
    public async IAsyncEnumerable<ClientEvent> Handle(
        SubscribeToClientEvents message,
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
// Small buffer for low-latency clients that consume quickly
await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(maxCapacity: 10, cancellationToken: ct))
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

## Integration with ASP.NET Core

### Streaming API Endpoints

```csharp
[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    private readonly IMediator _mediator;

    public DataController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("stream")]
    public async IAsyncEnumerable<DataItem> GetDataStream(
        [FromQuery] string filter,
        CancellationToken cancellationToken)
    {
        var query = new GetDataStreamQuery(filter);

    await foreach (var item in _mediator.Invoke<IAsyncEnumerable<DataItem>>(query, cancellationToken))
        {
            yield return item;
        }
    }

    [HttpGet("csv")]
    public async Task<IActionResult> ExportToCsv(
        [FromQuery] string filter,
        CancellationToken cancellationToken)
    {
        var query = new GetDataStreamQuery(filter);

        Response.Headers.Add("Content-Type", "text/csv");
        Response.Headers.Add("Content-Disposition", "attachment; filename=data.csv");

    await foreach (var item in _mediator.Invoke<IAsyncEnumerable<DataItem>>(query, cancellationToken))
        {
            var csv = $"{item.Id},{item.Name},{item.Value}\n";
            await Response.WriteAsync(csv, cancellationToken);
        }

        return new EmptyResult();
    }
}
```

### SignalR Integration

```csharp
public class LiveDataHub : Hub
{
    private readonly IMediator _mediator;

    public LiveDataHub(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task SubscribeToData(string filter)
    {
        var query = new GetLiveDataStreamQuery(filter);

    await foreach (var data in _mediator.Invoke<IAsyncEnumerable<LiveData>>(query, Context.ConnectionAborted))
        {
            await Clients.Caller.SendAsync("DataUpdate", data, Context.ConnectionAborted);
        }
    }
}
```

## Best Practices

### 1. Always Use CancellationToken

```csharp
public static async IAsyncEnumerable<T> Handle(
    StreamQuery query,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    // Always check for cancellation
    if (cancellationToken.IsCancellationRequested)
        yield break;

    // Pass cancellation token to async operations
    await foreach (var item in source.GetItemsAsync(cancellationToken))
    {
        yield return item;
    }
}
```

### 2. Handle Backpressure

```csharp
public static async IAsyncEnumerable<T> Handle(
    StreamQuery query,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    using var semaphore = new SemaphoreSlim(Environment.ProcessorCount);

    await foreach (var item in source.GetItemsAsync(cancellationToken))
    {
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            var processed = await ProcessItemAsync(item, cancellationToken);
            yield return processed;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
```

### 3. Provide Progress Reporting

```csharp
public static async IAsyncEnumerable<ProcessingResult<T>> Handle(
    StreamQuery query,
    IProgress<ProcessingProgress>? progress = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    var totalItems = await GetTotalItemCountAsync(query);
    var processedCount = 0;

    await foreach (var item in source.GetItemsAsync(cancellationToken))
    {
        var result = await ProcessItemAsync(item, cancellationToken);
        processedCount++;

        progress?.Report(new ProcessingProgress(processedCount, totalItems));

        yield return new ProcessingResult<T>(result, processedCount, totalItems);
    }
}
```

### 4. Implement Proper Cleanup

```csharp
public static async IAsyncEnumerable<T> Handle(
    StreamQuery query,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    IDisposable? resource = null;

    try
    {
        resource = await AcquireResourceAsync();

        await foreach (var item in ProcessWithResourceAsync(resource, query, cancellationToken))
        {
            yield return item;
        }
    }
    finally
    {
        resource?.Dispose();
    }
}
```

Streaming handlers are powerful for processing large datasets, real-time data feeds, and scenarios where you need to return results incrementally. They provide excellent memory efficiency and allow for responsive, scalable applications.

## Streaming Endpoints

Handlers that return `IAsyncEnumerable<T>` can automatically generate streaming HTTP endpoints. The source generator detects `IAsyncEnumerable<T>` return types and generates the appropriate endpoint code.

### Default Streaming (JSON Array)

By default, when a handler returns `IAsyncEnumerable<T>`, ASP.NET Core serializes the stream as a JSON array. The endpoint is automatically mapped as `GET`:

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

The generated endpoint returns the `IAsyncEnumerable<T>` directly — ASP.NET Core streams items as a JSON array without buffering the entire response.

### Server-Sent Events (SSE)

For real-time push scenarios, set `Streaming = EndpointStreaming.ServerSentEvents` to use ASP.NET Core's built-in `TypedResults.ServerSentEvents()` (requires .NET 10+):

```csharp
public record SubscribeToEvents;
public record ClientEvent(string EventType, object Data);

[Handler]
public class EventStreamHandler(IMediator mediator)
{
    [HandlerEndpoint(
        Route = "/events/stream",
        Streaming = EndpointStreaming.ServerSentEvents,
        SseEventType = "event",
        Summary = "Subscribe to real-time events via SSE")]
    [HandlerAllowAnonymous]
    public async IAsyncEnumerable<ClientEvent> Handle(
        SubscribeToEvents message,
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

The source generator produces:

```csharp
endpoints.MapGet("/events/stream", (...) =>
{
    var stream = ...Handler.Handle(mediator, message, cancellationToken);
    return TypedResults.ServerSentEvents(stream, eventType: "event");
})
    .Produces(200, contentType: "text/event-stream");
```

### SSE with Dynamic Subscriptions

For real-time event push, combine SSE endpoints with `SubscribeAsync`:

```csharp
[Handler]
public class EventStreamHandler(IMediator mediator)
{
    [HandlerEndpoint(
        Route = "/events/stream",
        Streaming = EndpointStreaming.ServerSentEvents,
        SseEventType = "event")]
    [HandlerAllowAnonymous]
    public async IAsyncEnumerable<ClientEvent> Handle(
        SubscribeToEvents message,
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

See [Dynamic Subscriptions with SubscribeAsync](#dynamic-subscriptions-with-subscribeasync) above for the full API.

### SSE Configuration Options

| Property | Type | Description |
|----------|------|-------------|
| `Streaming` | `EndpointStreaming` | `Default` (JSON array) or `ServerSentEvents` (SSE) |
| `SseEventType` | `string?` | Optional SSE event type passed to `TypedResults.ServerSentEvents()` |

### Client-Side Consumption

SSE endpoints are consumed using the browser's built-in `EventSource` API:

```typescript
const source = new EventSource('/events/stream');

source.addEventListener('event', (e) => {
    const data = JSON.parse(e.data);
    console.log('Received:', data);
});

source.onerror = () => {
    console.warn('SSE connection error, auto-reconnecting...');
};
```

`EventSource` automatically handles reconnection — no library like SignalR is needed.

### When to Use SSE vs Default Streaming

| Scenario                 | Recommended          |
| ------------------------ | -------------------- |
| One-time data export     | Default (JSON array) |
| Database query results   | Default (JSON array) |
| Real-time event feed     | SSE                  |
| Live notifications       | SSE                  |
| Progress updates         | SSE                  |
