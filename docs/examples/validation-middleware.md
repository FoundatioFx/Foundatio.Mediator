# Validation Middleware

This example demonstrates how to implement validation middleware that automatically validates messages using DataAnnotations and can short-circuit handler execution for invalid messages.

## Validation Middleware Implementation

Here's the complete validation middleware from the sample project:

@[code{7-45}](../samples/ConsoleSample/Middleware/ValidationMiddleware.cs)

## How It Works

### 1. Before Lifecycle Method

The validation runs in the `Before` method, which allows it to short-circuit handler execution:

```csharp
public static HandlerResult Before(object message)
{
    if (!TryValidate(message, out var errors))
    {
        // Convert validation errors to Result and short-circuit
        return Result.Invalid(errors);
    }

    // Continue to handler
    return HandlerResult.Continue();
}
```

### 2. DataAnnotations Validation

The middleware uses the standard .NET validation attributes:

```csharp
private static bool TryValidate(object message, out List<ValidationError> errors)
{
    errors = new List<ValidationError>();
    var context = new ValidationContext(message);
    var results = new List<ValidationResult>();

    if (Validator.TryValidateObject(message, context, results, true))
        return true;

    // Convert ValidationResult to ValidationError
    foreach (var result in results)
    {
        var propertyName = result.MemberNames.FirstOrDefault() ?? "Unknown";
        errors.Add(new ValidationError(propertyName, result.ErrorMessage ?? "Validation failed"));
    }

    return false;
}
```

### 3. Short-Circuiting

When validation fails, the middleware returns a `HandlerResult.ShortCircuit()` which:
- Prevents the handler from executing
- Returns the validation result directly to the caller
- Bypasses `After` middleware (since handler didn't run)
- Still runs `Finally` middleware for cleanup

## Message Validation

Here are the validated messages from the sample:

@[code{10-24}](../samples/ConsoleSample/Messages/Messages.cs)

### Validation Attributes Used

- `[Required]` - Ensures the property has a value
- `[StringLength]` - Validates string length with min/max
- `[Range]` - Validates numeric ranges
- Custom error messages for user-friendly feedback

## Usage Examples

### Valid Message

```csharp
var validOrder = new CreateOrder(
    CustomerId: "CUST-12345",
    Amount: 99.99m,
    Description: "Premium subscription service"
);

var result = await mediator.Invoke<Result<Order>>(validOrder);
// Validation passes, handler executes normally
```

### Invalid Message - Missing Required Field

```csharp
var invalidOrder = new CreateOrder(
    CustomerId: "", // ❌ Fails [Required] and [StringLength(MinimumLength = 3)]
    Amount: 99.99m,
    Description: "Test"
);

var result = await mediator.Invoke<Result<Order>>(invalidOrder);

// result.Status == ResultStatus.Invalid
// result.ValidationErrors contains:
// - PropertyName: "CustomerId", ErrorMessage: "Customer ID is required"
// - PropertyName: "CustomerId", ErrorMessage: "Customer ID must be between 3 and 50 characters"
```

### Invalid Message - Range Violation

```csharp
var invalidOrder = new CreateOrder(
    CustomerId: "CUST-001",
    Amount: -50m, // ❌ Fails [Range(0.01, 1000000)]
    Description: "Test"
);

var result = await mediator.Invoke<Result<Order>>(invalidOrder);

// result.Status == ResultStatus.Invalid
// result.ValidationErrors contains:
// - PropertyName: "Amount", ErrorMessage: "Amount must be between $0.01 and $1,000,000"
```

## Integration with Result Types

The validation middleware integrates seamlessly with Result types:

```csharp
public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
{
    var command = new CreateOrder(request.CustomerId, request.Amount, request.Description);
    var result = await _mediator.Invoke<Result<Order>>(command);

    return result.Status switch
    {
        ResultStatus.Created => CreatedAtAction(nameof(GetOrder),
            new { id = result.Value.Id }, result.Value),
        ResultStatus.Invalid => BadRequest(new
        {
            Message = "Validation failed",
            Errors = result.ValidationErrors.Select(e => new
            {
                Field = e.PropertyName,
                Message = e.ErrorMessage
            })
        }),
        _ => BadRequest(result.ErrorMessage)
    };
}
```

## Custom Validation Attributes

You can create custom validation attributes for complex business rules:

```csharp
public class ValidCustomerIdAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not string customerId)
            return false;

        // Custom business logic
        return customerId.StartsWith("CUST-") &&
               customerId.Length >= 8 &&
               customerId.All(c => char.IsLetterOrDigit(c) || c == '-');
    }

    public override string FormatErrorMessage(string name)
    {
        return $"{name} must be in format 'CUST-XXXXX'";
    }
}

// Usage
public record CreateOrder(
    [Required, ValidCustomerId]
    string CustomerId,

    [Required, Range(0.01, 1000000)]
    decimal Amount,

    [Required, StringLength(200, MinimumLength = 5)]
    string Description
);
```

## Conditional Validation

For more complex scenarios, you can implement conditional validation:

```csharp
public class ConditionalValidationMiddleware
{
    public static HandlerResult Before(object message)
    {
        // Only validate commands, not queries
        if (message is not ICommand)
            return HandlerResult.Continue();

        // Only validate in production
        if (Environment.IsDevelopment())
            return HandlerResult.Continue();

        if (!TryValidate(message, out var errors))
            return Result.Invalid(errors);

        return HandlerResult.Continue();
    }
}
```

## Validation with Async Rules

For validation that requires async operations (like database checks):

```csharp
public class AsyncValidationMiddleware
{
    private readonly ICustomerService _customerService;

    public AsyncValidationMiddleware(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    public async Task<HandlerResult> BeforeAsync(CreateOrder command, CancellationToken ct)
    {
        var errors = new List<ValidationError>();

        // Standard validation first
        if (!TryValidateSync(command, out var syncErrors))
            errors.AddRange(syncErrors);

        // Async validation
        if (!string.IsNullOrEmpty(command.CustomerId))
        {
            var customerExists = await _customerService.ExistsAsync(command.CustomerId, ct);
            if (!customerExists)
            {
                errors.Add(new ValidationError(nameof(command.CustomerId),
                    $"Customer {command.CustomerId} does not exist"));
            }
        }

        if (errors.Any())
            return Result.Invalid(errors);

        return HandlerResult.Continue();
    }
}
```

## Performance Considerations

### 1. Validation Order

Place validation middleware early in the pipeline to fail fast:

```csharp
[FoundatioOrder(10)]  // Run early
public class ValidationMiddleware { }

[FoundatioOrder(20)]  // Run after validation
public class LoggingMiddleware { }
```

### 2. Caching Validation Results

For expensive validation rules, consider caching:

```csharp
public class CachedValidationMiddleware
{
    private readonly IMemoryCache _cache;

    public HandlerResult Before(object message)
    {
        var cacheKey = $"validation:{message.GetType().Name}:{GetMessageHash(message)}";

        if (_cache.TryGetValue(cacheKey, out var cachedResult))
            return (HandlerResult)cachedResult;

        var result = ValidateMessage(message);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));

        return result;
    }
}
```

### 3. Selective Validation

Only validate specific message types:

```csharp
public class SelectiveValidationMiddleware
{
    public HandlerResult Before(ICommand command) // Only commands
    {
        return ValidateMessage(command);
    }

    // Queries and events pass through without validation
}
```

## Testing Validation Middleware

```csharp
[Test]
public void Should_Reject_Invalid_Message()
{
    // Arrange
    var invalidCommand = new CreateOrder("", -10m, "");

    // Act
    var result = ValidationMiddleware.Before(invalidCommand);

    // Assert
    result.IsShortCircuit.Should().BeTrue();
    result.Value.Should().BeOfType<Result>();

    var resultValue = (Result)result.Value;
    resultValue.Status.Should().Be(ResultStatus.Invalid);
    resultValue.ValidationErrors.Should().HaveCount(3);
}

[Test]
public void Should_Allow_Valid_Message()
{
    // Arrange
    var validCommand = new CreateOrder("CUST-001", 99.99m, "Valid description");

    // Act
    var result = ValidationMiddleware.Before(validCommand);

    // Assert
    result.ShouldContinue.Should().BeTrue();
}
```

## Best Practices

1. **Use standard DataAnnotations** when possible for consistency
2. **Provide clear error messages** that help users fix the issue
3. **Validate early** in the middleware pipeline to fail fast
4. **Consider async validation** for database-dependent rules
5. **Cache expensive validations** when appropriate
6. **Test validation rules** thoroughly with edge cases
7. **Use custom attributes** for complex business rules

## Next Steps

- [Result Types](../guide/result-types) - Understand validation error handling
- [Middleware Guide](../guide/middleware) - Learn more middleware patterns
