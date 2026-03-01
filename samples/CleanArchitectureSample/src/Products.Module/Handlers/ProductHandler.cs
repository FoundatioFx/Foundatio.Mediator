using Common.Module;
using Common.Module.Events;
using Common.Module.Middleware;
using Foundatio.Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Products.Module.Data;
using Products.Module.Domain;
using Products.Module.Messages;

namespace Products.Module.Handlers;

/// <summary>
/// Handles all product-related commands and queries.
/// Following Clean Architecture, this handler orchestrates use cases
/// and delegates persistence to the IProductRepository abstraction.
/// </summary>
[HandlerCategory("Products")]
public class ProductHandler(IProductRepository repository)
{
    /// <summary>
    /// Creates a new product (retries with custom inline settings)
    /// </summary>
    [Retry(MaxAttempts = 5, DelayMs = 200)]
    public async Task<(Result<Product>, ProductCreated?)> HandleAsync(CreateProduct command, CancellationToken cancellationToken)
    {
        var product = new Product(
            Id: Guid.NewGuid().ToString(),
            Name: command.Name,
            Description: command.Description,
            Price: command.Price,
            StockQuantity: command.StockQuantity,
            Status: command.StockQuantity > 0 ? ProductStatus.Active : ProductStatus.OutOfStock,
            CreatedAt: DateTime.UtcNow);

        await repository.AddAsync(product, cancellationToken);

        // Invalidate cached queries so the next list/get call returns fresh data
        CachingMiddleware.Invalidate(new GetProducts());

        // Return the product and an event that will be automatically published
        // Other modules can subscribe to ProductCreated without this module knowing about them
        return (product, new ProductCreated(product.Id, command.Name, command.Price, DateTime.UtcNow));
    }

    /// <summary>
    /// Gets a product by ID (anonymous - public catalog, cached 30s)
    /// </summary>
    [AllowAnonymous]
    [Cached(DurationSeconds = 30)]
    public async Task<Result<Product>> HandleAsync(GetProduct query, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
            return Result.NotFound($"Product {query.ProductId} not found");

        return product;
    }

    /// <summary>
    /// Gets all products (anonymous - public catalog, cached 30s)
    /// </summary>
    [AllowAnonymous]
    [Cached(DurationSeconds = 30)]
    public async Task<Result<List<Product>>> HandleAsync(GetProducts query, CancellationToken cancellationToken)
    {
        var products = await repository.GetAllAsync(cancellationToken);
        return products.ToList();
    }

    /// <summary>
    /// Updates an existing product
    /// </summary>
    public async Task<(Result<Product>, ProductUpdated?, ProductStockChanged?)> HandleAsync(UpdateProduct command, CancellationToken cancellationToken)
    {
        var existingProduct = await repository.GetByIdAsync(command.ProductId, cancellationToken);

        if (existingProduct is null)
            return (Result.NotFound($"Product {command.ProductId} not found"), null, null);

        var newStockQuantity = command.StockQuantity ?? existingProduct.StockQuantity;
        var stockChanged = newStockQuantity != existingProduct.StockQuantity;

        var updatedProduct = existingProduct with
        {
            Name = command.Name ?? existingProduct.Name,
            Description = command.Description ?? existingProduct.Description,
            Price = command.Price ?? existingProduct.Price,
            StockQuantity = newStockQuantity,
            Status = command.Status ?? (newStockQuantity > 0 ? ProductStatus.Active : ProductStatus.OutOfStock),
            UpdatedAt = DateTime.UtcNow
        };

        await repository.UpdateAsync(updatedProduct, cancellationToken);

        // Invalidate cached queries so the next list/get call returns fresh data
        CachingMiddleware.Invalidate(new GetProducts());
        CachingMiddleware.Invalidate(new GetProduct(command.ProductId));
        CachingMiddleware.Invalidate(new GetProductCatalog());

        // Return both events - ProductUpdated always, ProductStockChanged only if stock changed
        var updatedEvent = new ProductUpdated(command.ProductId, updatedProduct.Name, updatedProduct.Price, updatedProduct.Status.ToString(), DateTime.UtcNow);
        var stockEvent = stockChanged
            ? new ProductStockChanged(command.ProductId, existingProduct.StockQuantity, newStockQuantity, DateTime.UtcNow)
            : null;

        return (updatedProduct, updatedEvent, stockEvent);
    }

    /// <summary>
    /// Deletes a product
    /// </summary>
    public async Task<(Result, ProductDeleted?)> HandleAsync(DeleteProduct command, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(command.ProductId, cancellationToken);

        if (!deleted)
            return (Result.NotFound($"Product {command.ProductId} not found"), null);

        // Invalidate cached queries so the next list/get call returns fresh data
        CachingMiddleware.Invalidate(new GetProducts());
        CachingMiddleware.Invalidate(new GetProduct(command.ProductId));
        CachingMiddleware.Invalidate(new GetProductCatalog());

        return (Result.Success(), new ProductDeleted(command.ProductId, DateTime.UtcNow));
    }

    /// <summary>
    /// Returns an aggregated catalog summary.
    /// Simulates an expensive computation (500ms delay) that is cached for 60 seconds.
    /// First call is slow; subsequent calls return instantly from cache.
    /// </summary>
    [AllowAnonymous]
    [Cached(DurationSeconds = 60)]
    public async Task<Result<ProductCatalogSummary>> HandleAsync(
        GetProductCatalog query, ILogger<ProductHandler> logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("Computing product catalog summary (this is slow!)...");
        await Task.Delay(500, cancellationToken); // Simulate expensive aggregation

        var products = await repository.GetAllAsync(cancellationToken);
        var all = products.ToList();

        return new ProductCatalogSummary(
            TotalProducts: all.Count,
            ActiveProducts: all.Count(p => p.Status == ProductStatus.Active),
            AveragePrice: all.Count > 0 ? all.Average(p => p.Price) : 0m,
            GeneratedAt: DateTime.UtcNow);
    }

    /// <summary>
    /// Handles entity actions for products
    /// </summary>
    public async Task<Result> HandleAsync(EntityAction<Product> command, ILogger<ProductHandler> logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling entity action {Action} for product {ProductId}", command.Action, command.Entity.Id);

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
