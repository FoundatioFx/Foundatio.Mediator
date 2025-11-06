using Foundatio.Mediator;
using Microsoft.Extensions.Logging;

namespace Orders.Module.Middleware;

[Middleware(Order = 3)]
public static class OrdersModuleMiddleware
{
    public static void Before(object message, ILogger<IMediator> logger)
    {
        logger.LogInformation("OrdersModuleMiddleware Before handling {MessageType}", message.GetType().Name);
    }
}
