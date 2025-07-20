using Foundatio.Mediator.Benchmarks.Messages;

namespace Foundatio.Mediator.Benchmarks.Handlers.Foundatio;

// Foundatio.Mediator handlers using convention-based discovery
public class FoundatioPingHandler
{
    public async Task HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        // Simulate minimal work
        await Task.CompletedTask;
    }
}

public class FoundatioGreetingHandler
{
    public async Task<string> HandleAsync(GreetingQuery query, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return $"Hello, {query.Name}!";
    }
}

public class FoundatioCreateOrderHandler
{
    public async Task<string> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        // Simulate order creation
        await Task.CompletedTask;
        return $"Order-{Guid.NewGuid():N}";
    }
}

public class FoundatioGetOrderDetailsHandler
{
    public async Task<OrderDetails> HandleAsync(GetOrderDetailsQuery query)
    {
        // Simulate database lookup
        await Task.CompletedTask;
        return new OrderDetails(
            query.OrderId,
            "Product-123",
            1,
            99.99m,
            "Customer-456",
            DateTime.UtcNow
        );
    }
}

// Multiple handlers for the same notification (publish scenario)
public class FoundatioUserRegisteredEmailHandler
{
    public async Task HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Simulate sending email
        await Task.Delay(1, cancellationToken);
    }
}

public class FoundatioUserRegisteredAnalyticsHandler
{
    public async Task HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Simulate analytics tracking
        await Task.Delay(1, cancellationToken);
    }
}

public class FoundatioUserRegisteredWelcomeHandler
{
    public async Task HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Simulate welcome message
        await Task.Delay(1, cancellationToken);
    }
}
