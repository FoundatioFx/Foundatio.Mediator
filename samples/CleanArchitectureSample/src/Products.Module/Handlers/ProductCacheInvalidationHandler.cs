using Common.Module.Events;
using Common.Module.Middleware;
using Microsoft.Extensions.Logging;
using Products.Module.Messages;

namespace Products.Module.Handlers;

/// <summary>
/// Listens for distributed product events and invalidates the local in-memory cache.
/// Because <see cref="ProductCreated"/>, <see cref="ProductUpdated"/>, etc. implement
/// <c>IDistributedNotification</c>, they are replayed on every node via the pub/sub bus.
/// This handler ensures each node's <see cref="CachingMiddleware"/> cache stays consistent.
/// </summary>
public class ProductCacheInvalidationHandler(ILogger<ProductCacheInvalidationHandler> logger)
{
    public async Task HandleAsync(ProductCreated evt)
    {
        logger.LogInformation("Invalidating product caches for ProductCreated {ProductId}", evt.ProductId);
        await CachingMiddleware.InvalidateAsync(new GetProducts());
    }

    public async Task HandleAsync(ProductUpdated evt)
    {
        logger.LogInformation("Invalidating product caches for ProductUpdated {ProductId}", evt.ProductId);
        await CachingMiddleware.InvalidateAsync(new GetProducts());
        await CachingMiddleware.InvalidateAsync(new GetProduct(evt.ProductId));
        await CachingMiddleware.InvalidateAsync(new GetProductCatalog());
    }

    public async Task HandleAsync(ProductDeleted evt)
    {
        logger.LogInformation("Invalidating product caches for ProductDeleted {ProductId}", evt.ProductId);
        await CachingMiddleware.InvalidateAsync(new GetProducts());
        await CachingMiddleware.InvalidateAsync(new GetProduct(evt.ProductId));
        await CachingMiddleware.InvalidateAsync(new GetProductCatalog());
    }

    public async Task HandleAsync(ProductStockChanged evt)
    {
        logger.LogInformation("Invalidating product caches for ProductStockChanged {ProductId}", evt.ProductId);
        await CachingMiddleware.InvalidateAsync(new GetProduct(evt.ProductId));
        await CachingMiddleware.InvalidateAsync(new GetProductCatalog());
    }
}
