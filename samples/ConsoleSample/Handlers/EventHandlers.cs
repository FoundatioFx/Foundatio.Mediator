using ConsoleSample.Messages;
using Microsoft.Extensions.Logging;

namespace ConsoleSample.Handlers;

// Event handlers for publish pattern demonstration
public class OrderEventHandlers
{
    private readonly ILogger<OrderEventHandlers> _logger;

    public OrderEventHandlers(ILogger<OrderEventHandlers> logger)
    {
        _logger = logger;
    }

    // Multiple handlers for the same event (publish pattern)
    public void Handle(OrderCreated orderCreated)
    {
        _logger.LogInformation("📧 Sending order confirmation email for order {OrderId}", orderCreated.OrderId);
        Console.WriteLine($"📧 Email sent: Order {orderCreated.OrderId} confirmed for ${orderCreated.Amount:F2}");
    }

    public void Handle(OrderUpdated orderUpdated)
    {
        _logger.LogInformation("📧 Sending order update notification for order {OrderId}", orderUpdated.OrderId);
        Console.WriteLine($"📧 Email sent: Order {orderUpdated.OrderId} updated with amount ${orderUpdated.Amount:F2}");
    }

    public void Handle(OrderDeleted orderDeleted)
    {
        _logger.LogInformation("📧 Sending order cancellation email for order {OrderId}", orderDeleted.OrderId);
        Console.WriteLine($"📧 Email sent: Order {orderDeleted.OrderId} has been cancelled");
    }
}

public class OrderNotificationHandler
{
    private readonly ILogger<OrderNotificationHandler> _logger;

    public OrderNotificationHandler(ILogger<OrderNotificationHandler> logger)
    {
        _logger = logger;
    }

    public void Handle(OrderCreated orderCreated)
    {
        _logger.LogInformation("📱 Sending SMS notification for order {OrderId}", orderCreated.OrderId);
        Console.WriteLine($"📱 SMS sent: Your order {orderCreated.OrderId} is being processed!");
    }

    public void Handle(OrderUpdated orderUpdated)
    {
        _logger.LogInformation("📱 Sending SMS update for order {OrderId}", orderUpdated.OrderId);
        Console.WriteLine($"📱 SMS sent: Order {orderUpdated.OrderId} has been updated");
    }
}

public class OrderAuditHandler
{
    private readonly ILogger<OrderAuditHandler> _logger;

    public OrderAuditHandler(ILogger<OrderAuditHandler> logger)
    {
        _logger = logger;
    }

    public void Handle(OrderCreated orderCreated)
    {
        _logger.LogInformation("📋 Audit: Order created - {OrderId} for customer {CustomerId}",
            orderCreated.OrderId, orderCreated.CustomerId);
        Console.WriteLine($"📋 Audit: Order {orderCreated.OrderId} created for customer {orderCreated.CustomerId}");
    }

    public void Handle(OrderUpdated orderUpdated)
    {
        _logger.LogInformation("📋 Audit: Order updated - {OrderId}", orderUpdated.OrderId);
        Console.WriteLine($"📋 Audit: Order {orderUpdated.OrderId} updated");
    }

    public void Handle(OrderDeleted orderDeleted)
    {
        _logger.LogInformation("📋 Audit: Order deleted - {OrderId}", orderDeleted.OrderId);
        Console.WriteLine($"📋 Audit: Order {orderDeleted.OrderId} deleted");
    }
}
