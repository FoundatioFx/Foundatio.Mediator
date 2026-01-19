using Common.Module;
using Common.Module.Events;
using Foundatio.Mediator;
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
[HandlerCategory("Products", RoutePrefix = "/api/products")]
public class ProductHandler(IProductRepository repository)
{
    /// <summary>
    /// Creates a new product
    /// </summary>
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

        // Return the product and an event that will be automatically published
        // Other modules can subscribe to ProductCreated without this module knowing about them
        return (product, new ProductCreated(product.Id, command.Name, command.Price, DateTime.UtcNow));
    }

    /// <summary>
    /// Gets a product by ID
    /// </summary>
    public async Task<Result<Product>> HandleAsync(GetProduct query, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
            return Result.NotFound($"Product {query.ProductId} not found");

        return product;
    }

    /// <summary>
    /// Gets all products
    /// </summary>
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

        return (Result.Success(), new ProductDeleted(command.ProductId, DateTime.UtcNow));
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
