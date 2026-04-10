using Common.Module.Middleware;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;

namespace Orders.Module.Middleware;

[Middleware(OrderAfter = [typeof(ValidationMiddleware)])]
public static class OrdersModuleMiddleware
{
    public static void Before(object message, ILogger<IMediator> logger)
    {
        logger.LogInformation("OrdersModuleMiddleware Before handling {MessageType}", message.GetType().Name);
    }
}
