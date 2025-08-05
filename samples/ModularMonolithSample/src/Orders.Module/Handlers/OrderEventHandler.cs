using Microsoft.Extensions.Logging;
using Orders.Module.Messages;

namespace Orders.Module.Handlers;

public class OrderEventHandler(ILogger<OrderEventHandler> logger)
{
    public Task HandleAsync(OrderCreated evt)
    {
        logger.LogInformation("Order {OrderId} created for customer {CustomerId} with amount {Amount:C}",
            evt.OrderId, evt.CustomerId, evt.Amount);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderUpdated evt)
    {
        logger.LogInformation("Order {OrderId} updated with amount {Amount:C}",
            evt.OrderId, evt.Amount);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderDeleted evt)
    {
        logger.LogInformation("Order {OrderId} deleted", evt.OrderId);
        return Task.CompletedTask;
    }
}
