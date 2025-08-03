using ConsoleSample.Messages;
using ConsoleSample.Services;

namespace ConsoleSample.Handlers;

public class OrderEmailNotificationHandler(EmailNotificationService emailService)
{
    public async Task HandleAsync(OrderCreatedEvent orderEvent)
    {
        string message = $"Order {orderEvent.OrderId} has been created for ${orderEvent.Amount:F2}";
        await emailService.SendAsync(message);
    }
}

public class OrderAuditHandler
{
    public async Task HandleAsync(
        OrderCreatedEvent orderEvent,
        IAuditService auditService,
        CancellationToken cancellationToken = default)
    {
        await auditService.LogEventAsync("OrderCreated", orderEvent);
    }
}

public struct Stuff();

public class CreateOrderHandler
{
    public async Task<(Order Order, OrderCreatedEvent OrderCreated, Stuff Stuff)> HandleAsync(
        CreateOrder command,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🔄 Processing order {command.OrderId}");
        await Task.Delay(100, cancellationToken); // Simulate processing

        var order = new Order(command.OrderId, command.CustomerId, command.Amount, command.ProductName);
        return (order, new OrderCreatedEvent(
            command.OrderId,
            command.CustomerId,
            command.Amount,
            command.ProductName
        ), new Stuff());
    }
}
