using Foundatio.Mediator;
using Microsoft.Extensions.Logging;

namespace Products.Module.Middleware;

[Middleware(Order = 3)]
public static class ProductsModuleMiddleware
{
    public static void Before(object message, ILogger<IMediator> logger)
    {
        logger.LogInformation("ProductsModuleMiddleware Before handling {MessageType}", message.GetType().Name);
    }
}
