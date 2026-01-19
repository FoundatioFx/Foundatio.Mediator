using Common.Module;
using Common.Module.Events;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;
using Orders.Module.Data;
using Orders.Module.Domain;
using Orders.Module.Messages;

namespace Orders.Module.Handlers;

/// <summary>
/// Handles all order-related commands and queries.
/// Following Clean Architecture, this handler orchestrates use cases
/// and delegates persistence to the IOrderRepository abstraction.
/// </summary>
[HandlerCategory("Orders", RoutePrefix = "/api/orders")]
public class OrderHandler(IOrderRepository repository)
{
    /// <summary>
    /// Creates a new order
    /// </summary>
    public async Task<(Result<Order>, OrderCreated?)> HandleAsync(CreateOrder command, CancellationToken cancellationToken)
    {
        var order = new Order(
            Id: Guid.NewGuid().ToString(),
            CustomerId: command.CustomerId,
            Amount: command.Amount,
            Description: command.Description,
            Status: OrderStatus.Pending,
            CreatedAt: DateTime.UtcNow);

        await repository.AddAsync(order, cancellationToken);

        // Return the order and an event that will be automatically published
        // Other modules can subscribe to OrderCreated without this module knowing about them
        return (order, new OrderCreated(order.Id, command.CustomerId, command.Amount, DateTime.UtcNow));
    }

    /// <summary>
    /// Gets an order by ID
    /// </summary>
    public async Task<Result<Order>> HandleAsync(GetOrder query, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(query.OrderId, cancellationToken);

        if (order is null)
            return Result.NotFound($"Order {query.OrderId} not found");

        return order;
    }

    /// <summary>
    /// Gets all orders
    /// </summary>
    public async Task<Result<List<Order>>> HandleAsync(GetOrders query, CancellationToken cancellationToken)
    {
        var orders = await repository.GetAllAsync(cancellationToken);
        return orders.ToList();
    }

    /// <summary>
    /// Updates an existing order
    /// </summary>
    public async Task<(Result<Order>, OrderUpdated?)> HandleAsync(UpdateOrder command, CancellationToken cancellationToken)
    {
        var existingOrder = await repository.GetByIdAsync(command.OrderId, cancellationToken);

        if (existingOrder is null)
            return (Result.NotFound($"Order {command.OrderId} not found"), null);

        var updatedOrder = existingOrder with
        {
            Amount = command.Amount ?? existingOrder.Amount,
            Description = command.Description ?? existingOrder.Description,
            Status = command.Status ?? existingOrder.Status,
            UpdatedAt = DateTime.UtcNow
        };

        await repository.UpdateAsync(updatedOrder, cancellationToken);

        return (updatedOrder, new OrderUpdated(command.OrderId, updatedOrder.Amount, updatedOrder.Status.ToString(), DateTime.UtcNow));
    }

    /// <summary>
    /// Deletes an order
    /// </summary>
    public async Task<(Result, OrderDeleted?)> HandleAsync(DeleteOrder command, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(command.OrderId, cancellationToken);

        if (!deleted)
            return (Result.NotFound($"Order {command.OrderId} not found"), null);

        return (Result.Success(), new OrderDeleted(command.OrderId, DateTime.UtcNow));
    }

    /// <summary>
    /// Handles entity actions for orders
    /// </summary>
    public async Task<Result> HandleAsync(EntityAction<Order> command, ILogger<OrderHandler> logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling entity action {Action} for order {OrderId}", command.Action, command.Entity.Id);

        // Example: could route to specific handlers based on action type
        return command.Action switch
        {
            EntityActionType.Create => Result.Success(),
            EntityActionType.Update => Result.Success(),
            EntityActionType.Delete => Result.Success(),
            _ => Result.Error($"Unknown action: {command.Action}")
        };
    }
}
