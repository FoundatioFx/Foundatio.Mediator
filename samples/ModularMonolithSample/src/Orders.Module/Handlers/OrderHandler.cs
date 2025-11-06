using Common.Module;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;
using Orders.Module.Messages;

namespace Orders.Module.Handlers;

public class OrderHandler
{
    private static readonly Dictionary<string, Order> _orders = new();

    public async Task<(Result<Order>, OrderCreated?)> HandleAsync(CreateOrder command)
    {
        string orderId = Guid.NewGuid().ToString();
        var order = new Order(
            orderId,
            command.CustomerId,
            command.Amount,
            command.Description,
            DateTime.UtcNow);

        _orders[orderId] = order;

        await Task.CompletedTask; // Simulate async work

        return (order, new OrderCreated(orderId, command.CustomerId, command.Amount, DateTime.UtcNow));
    }

    public async Task<Result<Order>> HandleAsync(GetOrder query)
    {
        if (!_orders.TryGetValue(query.OrderId, out var order))
            return Result.NotFound($"Order {query.OrderId} not found");

        await Task.CompletedTask; // Simulate async work

        return order;
    }

    public async Task<Result<List<Order>>> HandleAsync(GetOrders query)
    {
        await Task.CompletedTask; // Simulate async work
        return _orders.Values.ToList();
    }

    public async Task<(Result<Order>, OrderUpdated?)> HandleAsync(UpdateOrder command)
    {
        if (!_orders.TryGetValue(command.OrderId, out var existingOrder))
            return (Result.NotFound($"Order {command.OrderId} not found"), null);

        var updatedOrder = existingOrder with
        {
            Amount = command.Amount ?? existingOrder.Amount,
            Description = command.Description ?? existingOrder.Description,
            UpdatedAt = DateTime.UtcNow
        };

        _orders[command.OrderId] = updatedOrder;

        await Task.CompletedTask; // Simulate async work

        return (updatedOrder, new OrderUpdated(command.OrderId, updatedOrder.Amount, DateTime.UtcNow));
    }

    public async Task<(Result, OrderDeleted?)> HandleAsync(DeleteOrder command)
    {
        if (!_orders.Remove(command.OrderId, out _))
            return (Result.NotFound($"Order {command.OrderId} not found"), null);

        await Task.CompletedTask; // Simulate async work

        return (Result.Success(), new OrderDeleted(command.OrderId, DateTime.UtcNow));
    }

    public async Task<Result> HandleAsync(EntityAction<Order> command, ILogger<OrderHandler> logger)
    {
        logger.LogInformation("Handling entity action {Action} for order {OrderId}: {TypeName}", command.Action, command.Entity.Id, MessageTypeKey.Get(typeof(EntityAction<Order>)));
        await Task.CompletedTask; // Simulate async work

        return Result.Success();
    }
}
