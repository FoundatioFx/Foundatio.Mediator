using ConsoleSample.Messages;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;

namespace ConsoleSample.Handlers;

// Simple handler
public static class SimpleHandler
{
    public static void Handle(Ping ping)
    {
        Console.WriteLine($"🏓 Ping received: {ping.Text}");
    }

    public static string Handle(GetGreeting greeting)
    {
        return $"Hello, {greeting.Name}!";
    }
}

// Order CRUD handlers with Result pattern
public class OrderHandler
{
    private static readonly Dictionary<string, Order> _orders = new();
    private readonly ILogger<OrderHandler> _logger;
    private readonly IMediator _mediator;

    public OrderHandler(ILogger<OrderHandler> logger, IMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    public async Task<Result<Order>> HandleAsync(CreateOrder command)
    {
        _logger.LogInformation("Creating order for customer {CustomerId} with amount {Amount}",
            command.CustomerId, command.Amount);

        // Validation
        if (string.IsNullOrWhiteSpace(command.CustomerId))
            return Result<Order>.Invalid(new ValidationError("CustomerId", "Customer ID is required"));

        if (command.Amount <= 0)
            return Result<Order>.Invalid(new ValidationError("Amount", "Amount must be greater than zero"));

        var orderId = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var order = new Order(orderId, command.CustomerId, command.Amount, command.Description, DateTime.UtcNow);

        _orders[orderId] = order;

        // Publish event for other handlers to react
        await _mediator.PublishAsync(new OrderCreated(orderId, command.CustomerId, command.Amount, order.CreatedAt));

        return Result<Order>.Created(order, $"/orders/{orderId}");
    }

    public Result<Order> Handle(GetOrder query)
    {
        _logger.LogInformation("Getting order {OrderId}", query.OrderId);

        if (!_orders.TryGetValue(query.OrderId, out var order))
        {
            return Result<Order>.NotFound($"Order {query.OrderId} not found");
        }

        return order; // Implicit conversion to Result<Order>
    }

    public async Task<Result<Order>> HandleAsync(UpdateOrder command)
    {
        _logger.LogInformation("Updating order {OrderId}", command.OrderId);

        if (!_orders.TryGetValue(command.OrderId, out var existingOrder))
        {
            return Result<Order>.NotFound($"Order {command.OrderId} not found");
        }

        var updatedOrder = existingOrder with
        {
            Amount = command.Amount ?? existingOrder.Amount,
            Description = command.Description ?? existingOrder.Description,
            UpdatedAt = DateTime.UtcNow
        };

        _orders[command.OrderId] = updatedOrder;

        // Publish event
        await _mediator.PublishAsync(new OrderUpdated(command.OrderId, updatedOrder.Amount, updatedOrder.UpdatedAt.Value));

        return updatedOrder;
    }

    public async Task<Result> HandleAsync(DeleteOrder command)
    {
        _logger.LogInformation("Deleting order {OrderId}", command.OrderId);

        if (!_orders.ContainsKey(command.OrderId))
        {
            return Result.NotFound($"Order {command.OrderId} not found");
        }

        _orders.Remove(command.OrderId);

        // Publish event
        await _mediator.PublishAsync(new OrderDeleted(command.OrderId, DateTime.UtcNow));

        return Result.NoContent();
    }
}
