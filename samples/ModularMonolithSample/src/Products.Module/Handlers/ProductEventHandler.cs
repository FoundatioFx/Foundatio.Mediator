using Microsoft.Extensions.Logging;
using Products.Module.Messages;

namespace Products.Module.Handlers;

public class ProductEventHandler(ILogger<ProductEventHandler> logger)
{
    public Task HandleAsync(ProductCreated evt)
    {
        logger.LogInformation("Product {ProductId} created with name {Name} and price {Price:C}",
            evt.ProductId, evt.Name, evt.Price);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ProductUpdated evt)
    {
        logger.LogInformation("Product {ProductId} updated with name {Name} and price {Price:C}",
            evt.ProductId, evt.Name, evt.Price);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ProductDeleted evt)
    {
        logger.LogInformation("Product {ProductId} deleted", evt.ProductId);
        return Task.CompletedTask;
    }
}
