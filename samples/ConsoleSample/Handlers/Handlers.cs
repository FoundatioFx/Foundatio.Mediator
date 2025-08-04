using System.Runtime.CompilerServices;
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

// Order CRUD handlers with Result pattern and middleware
public class OrderHandler
{
    private static readonly Dictionary<string, Order> _orders = new();
    private readonly ILogger<OrderHandler> _logger;

    public OrderHandler(ILogger<OrderHandler> logger)
    {
        _logger = logger;
    }

    public async Task<(Result<Order> Order, OrderCreated? Event)> HandleAsync(CreateOrder command)
    {
        _logger.LogInformation("Creating order for customer {CustomerId} with amount {Amount}",
            command.CustomerId, command.Amount);

        var orderId = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var order = new Order(orderId, command.CustomerId, command.Amount, command.Description, DateTime.UtcNow);

        _orders[orderId] = order;

        await Task.CompletedTask; // Simulate async operation

        return (Result<Order>.Created(order, $"/orders/{orderId}"), new OrderCreated(orderId, command.CustomerId, command.Amount, order.CreatedAt));
    }

    public Result<Order> Handle(GetOrder query)
    {
        _logger.LogInformation("Getting order {OrderId}", query.OrderId);

        if (!_orders.TryGetValue(query.OrderId, out var order))
        {
            return Result.NotFound($"Order {query.OrderId} not found");
        }

        return order; // Implicit conversion to Result<Order>
    }

    public async Task<(Result<Order> Order, OrderUpdated? Event)> HandleAsync(UpdateOrder command)
    {
        _logger.LogInformation("Updating order {OrderId}", command.OrderId);

        if (!_orders.TryGetValue(command.OrderId, out var existingOrder))
        {
            return (Result.NotFound($"Order {command.OrderId} not found"), null);
        }

        var updatedOrder = existingOrder with
        {
            Amount = command.Amount ?? existingOrder.Amount,
            Description = command.Description ?? existingOrder.Description,
            UpdatedAt = DateTime.UtcNow
        };

        _orders[command.OrderId] = updatedOrder;

        await Task.CompletedTask; // Simulate async operation

        return (updatedOrder, new OrderUpdated(command.OrderId, updatedOrder.Amount, updatedOrder.UpdatedAt.Value));
    }

    public async Task<(Result Order, OrderDeleted? Event)> HandleAsync(DeleteOrder command)
    {
        _logger.LogInformation("Deleting order {OrderId}", command.OrderId);

        if (!_orders.ContainsKey(command.OrderId))
        {
            return (Result.NotFound($"Order {command.OrderId} not found"), null);
        }

        _orders.Remove(command.OrderId);

        await Task.CompletedTask; // Simulate async operation

        return (Result.NoContent(), new OrderDeleted(command.OrderId, DateTime.UtcNow));
    }
}

// Streaming handler example
public class StreamingHandler
{
    private readonly ILogger<StreamingHandler> _logger;

    public StreamingHandler(ILogger<StreamingHandler> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<int> HandleAsync(CounterStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < 10; i++)
        {
            _logger.LogInformation("Streaming value: {Value}", i);

            if (cancellationToken.IsCancellationRequested)
                yield break;

            await Task.Delay(1000, cancellationToken);
            yield return i;
        }
    }
}
