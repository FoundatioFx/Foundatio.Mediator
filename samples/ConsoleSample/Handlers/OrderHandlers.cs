using ConsoleSample.Messages;
using ConsoleSample.Services;

namespace ConsoleSample.Handlers;

// Multiple handlers for the same event (for Publish pattern)
public class OrderEmailNotificationHandler(EmailNotificationService emailService)
{
    public async Task HandleAsync(OrderCreatedEvent orderEvent)
    {
        string message = $"Order {orderEvent.OrderId} has been created for ${orderEvent.Amount:F2}";
        await emailService.SendAsync(message);
    }
}

public class OrderSmsNotificationHandler
{
    public async Task HandleAsync(
        OrderCreatedEvent orderEvent,
        SmsNotificationService smsService,
        CancellationToken cancellationToken = default)
    {
        string message = $"Your order {orderEvent.OrderId} is confirmed!";
        await smsService.SendAsync(message);
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

// Single handler for Invoke pattern
public class ProcessOrderHandler
{
    public async Task<string> HandleAsync(
        ProcessOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🔄 Processing order {command.OrderId} with type: {command.ProcessingType}");
        await Task.Delay(100, cancellationToken); // Simulate processing
        return $"Order {command.OrderId} processed successfully with {command.ProcessingType}";
    }
}
