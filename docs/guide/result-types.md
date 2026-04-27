# Result Types

Foundatio Mediator includes built-in `Result` and `Result<T>` types for robust error handling without relying on exceptions. These discriminated union types capture success, validation errors, conflicts, not found states, and more.

## Why Result Types?

Traditional .NET applications often use exceptions for control flow, which can be expensive and make it difficult to handle expected error conditions gracefully. Result types provide a better alternative:

- **Performance**: No exception overhead for expected failures
- **Explicit**: Return types clearly indicate potential failure modes
- **Composable**: Easy to chain operations and handle errors functionally
- **Testable**: Straightforward to test all code paths

## Basic Result Usage

### Result (Non-Generic)

For operations that don't return data but can fail:

```csharp
public Result Handle(DeleteOrder command)
{
    if (!_orders.ContainsKey(command.OrderId))
        return Result.NotFound($"Order {command.OrderId} not found");

    _orders.Remove(command.OrderId);
    return Result.NoContent(); // Success with no content
}
```

### Result&lt;T&gt; (Generic)

For operations that return data or can fail:

```csharp
public Result<Order> Handle(GetOrder query)
{
    if (!_orders.TryGetValue(query.OrderId, out var order))
        return Result.NotFound($"Order {query.OrderId} not found");

    return order; // Implicit conversion to Result<Order>
}
```

## Result Status Types

Result types include several built-in status types:

```csharp
public enum ResultStatus
{
    Success,
    Created,
    NoContent,
    BadRequest,
    Error,
    Invalid,
    NotFound,
    Unauthorized,
    Forbidden,
    Conflict,
    CriticalError,
    Unavailable
}
```

## Creating Results

### Success Results

```csharp
// Simple success
return Result.Success();
return Result<User>.Success(user);

// Created with location
return Result<Order>.Created(order, $"/orders/{order.Id}");

// No content (for deletions)
return Result.NoContent();

// Implicit conversion from value
return user; // Automatically becomes Result<User>.Success(user)
```

### File Results

For handlers that return file downloads:

```csharp
// From a stream
return Result.File(fileStream, "application/pdf", "report.pdf");

// From a byte array
return Result.File(bytes, "image/png", "photo.png");

// Inline (no download prompt) — omit the file name
return Result.File(stream, "image/jpeg");
```

`Result.File()` returns a `Result<FileResult>`. The `FileResult` class carries:

| Property | Type | Default | Description |
| ------------- | ---------- | ----------------------------- | ----------------------------------------------------------------- |
| `Stream` | `Stream` | `Stream.Null` | The file content |
| `ContentType` | `string` | `"application/octet-stream"` | MIME type |
| `FileName` | `string?` | `null` | When set, triggers a `Content-Disposition: attachment` header |

When used with [generated endpoints](/guide/endpoints), `Result<FileResult>` is automatically mapped to `Results.File(stream, contentType, fileName)` instead of `Results.Ok()`.

### Error Results

```csharp
// Not found
return Result.NotFound("User not found");
return Result.NotFound($"Order {orderId} not found");

// Validation errors
return Result.Invalid("Name is required");
return Result.Invalid(validationErrors);

// Forbidden access
return Result.Forbidden("Insufficient permissions");

// Conflict (e.g., duplicate key)
return Result.Conflict("Email already exists");

// Generic error
return Result.Error("Something went wrong");
```

## Working with Results

### Checking Success

```csharp
var result = await mediator.InvokeAsync<Result<User>>(new GetUser(123));

if (result.IsSuccess)
{
    var user = result.Value;
    Console.WriteLine($"Found user: {user.Name}");
}
else
{
    Console.WriteLine($"Error: {result.Message}");
}
```

### Pattern Matching

```csharp
var result = await mediator.InvokeAsync<Result<Order>>(new GetOrder("123"));

var message = result.Status switch
{
    ResultStatus.Ok => $"Order: {result.Value.Description}",
    ResultStatus.NotFound => "Order not found",
    ResultStatus.Forbidden => "Access denied",
    _ => $"Error: {result.Message}"
};
```

### Accessing Properties

```csharp
public class Result<T>
{
    public bool IsSuccess { get; }
    public ResultStatus Status { get; }
    public T Value { get; }                    // throws if !IsSuccess
    public T ValueOrDefault { get; }           // returns default(T) if !IsSuccess
    public string Message { get; }
    public IEnumerable<ValidationError> ValidationErrors { get; }
}
```

## Validation Errors

Result types support detailed validation errors:

```csharp
public Result<User> Handle(CreateUser command)
{
    var errors = new List<ValidationError>();

    if (string.IsNullOrEmpty(command.Name))
        errors.Add(ValidationError.Create("Name", "Name is required"));

    if (command.Age < 0)
        errors.Add(ValidationError.Create("Age", "Age must be positive"));

    if (errors.Any())
        return Result.Invalid(errors);

    var user = new User(command.Name, command.Age);
    return user;
}
```

## Integration with ASP.NET Core

When using [endpoint generation](/guide/endpoints), `Result<T>` and `Result` are automatically converted to the correct HTTP status codes — no manual mapping needed. See [Result to HTTP Status Mapping](/guide/endpoints#result-to-http-status-mapping) for the default mapping table.

To customize the mapping, implement `IMediatorResultMapper<IResult>` and register it before `AddMediator()`. See [Custom Result Mapping](/guide/endpoints#custom-result-mapping) for details.

### Manual Mapping (Controllers)

If you use traditional controllers instead of endpoint generation, you can map results manually:

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrder(string id)
    {
        var result = await _mediator.InvokeAsync<Result<Order>>(new GetOrder(id));

        return result.Status switch
        {
            ResultStatus.Ok => Ok(result.Value),
            ResultStatus.NotFound => NotFound(result.ErrorMessage),
            ResultStatus.Forbidden => Forbid(),
            _ => BadRequest(result.ErrorMessage)
        };
    }

    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrder(CreateOrder command)
    {
        var result = await _mediator.InvokeAsync<Result<Order>>(command);

        return result.Status switch
        {
            ResultStatus.Created => CreatedAtAction(nameof(GetOrder),
                new { id = result.Value.Id }, result.Value),
            ResultStatus.Invalid => BadRequest(result.ValidationErrors),
            ResultStatus.Conflict => Conflict(result.ErrorMessage),
            _ => BadRequest(result.ErrorMessage)
        };
    }
}
```

## Extension Methods

You can create extension methods to make Result handling more convenient:

```csharp
public static class ResultExtensions
{
    public static ActionResult ToActionResult<T>(this Result<T> result)
    {
        return result.Status switch
        {
            ResultStatus.Ok => new OkObjectResult(result.Value),
            ResultStatus.Created => new CreatedResult("", result.Value),
            ResultStatus.NoContent => new NoContentResult(),
            ResultStatus.NotFound => new NotFoundObjectResult(result.ErrorMessage),
            ResultStatus.Invalid => new BadRequestObjectResult(result.ValidationErrors),
            ResultStatus.Forbidden => new ForbidResult(),
            ResultStatus.Conflict => new ConflictObjectResult(result.ErrorMessage),
            _ => new BadRequestObjectResult(result.ErrorMessage)
        };
    }
}

// Usage
[HttpGet("{id}")]
public async Task<ActionResult> GetOrder(string id)
{
    var result = await _mediator.InvokeAsync<Result<Order>>(new GetOrder(id));
    return result.ToActionResult();
}
```

## Best Practices

### 1. Be Specific with Error Messages

```csharp
// ❌ Generic
return Result.NotFound("Not found");

// ✅ Specific
return Result.NotFound($"Order {orderId} not found");
```

### 2. Use Appropriate Status Codes

```csharp
// For business rule violations
return Result.Conflict("Cannot delete order with pending payments");

// For authorization failures
return Result.Forbidden("User cannot access other users' orders");

// For validation failures
return Result.Invalid("Email format is invalid");
```

### 3. Handle All Result Cases

```csharp
// ❌ Only checking IsSuccess
if (result.IsSuccess)
    return result.Value;
// What about errors?

// ✅ Pattern matching all cases
return result.Status switch
{
    ResultStatus.Ok => result.Value,
    ResultStatus.NotFound => throw new NotFoundException(result.Message),
    _ => throw new InvalidOperationException(result.Message)
};
```

### 4. Compose Results

```csharp
public async Task<Result<OrderSummary>> Handle(GetOrderSummary query)
{
    var orderResult = await _mediator.InvokeAsync<Result<Order>>(new GetOrder(query.OrderId));
    if (!orderResult.IsSuccess)
        return Result<OrderSummary>.FromResult(orderResult);

    var customerResult = await _mediator.InvokeAsync<Result<Customer>>(new GetCustomer(orderResult.Value.CustomerId));
    if (!customerResult.IsSuccess)
        return Result<OrderSummary>.FromResult(customerResult);

    var summary = new OrderSummary(orderResult.Value, customerResult.Value);
    return summary;
}
```

## Next Steps

- [Handler Conventions](/guide/handler-conventions) - See Result types in handler return values
- [Middleware](/guide/middleware) - Middleware patterns including validation
- [Handler Conventions](/guide/handler-conventions) - Learn handler return type rules
