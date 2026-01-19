using Common.Module;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;
using Products.Module.Messages;

namespace Products.Module.Handlers;

[HandlerCategory("Products")]
public class ProductHandler
{
    private static readonly Dictionary<string, Product> _products = new();

    /// <summary>
    /// Creates a new product
    /// </summary>
    public async Task<(Result<Product>, ProductCreated?)> HandleAsync(CreateProduct command)
    {
        string productId = Guid.NewGuid().ToString();
        var product = new Product(
            productId,
            command.Name,
            command.Price,
            command.Description,
            DateTime.UtcNow);

        _products[productId] = product;

        await Task.CompletedTask; // Simulate async work

        return (product, new ProductCreated(productId, command.Name, command.Price, DateTime.UtcNow));
    }

    /// <summary>
    /// Gets a product by ID
    /// </summary>
    public Result<Product> Handle(GetProduct query)
    {
        if (!_products.TryGetValue(query.ProductId, out var product))
            return Result.NotFound($"Product {query.ProductId} not found");

        //await Task.CompletedTask; // Simulate async work

        return product;
    }

    /// <summary>
    /// Gets all products
    /// </summary>
    public async Task<Result<List<Product>>> HandleAsync(GetProducts query)
    {
        await Task.CompletedTask; // Simulate async work
        return _products.Values.ToList();
    }

    /// <summary>
    /// Updates an existing product
    /// </summary>
    public async Task<(Result<Product>, ProductUpdated?)> HandleAsync(UpdateProduct command)
    {
        if (!_products.TryGetValue(command.ProductId, out var existingProduct))
            return (Result.NotFound($"Product {command.ProductId} not found"), null);

        var updatedProduct = existingProduct with
        {
            Name = command.Name ?? existingProduct.Name,
            Price = command.Price ?? existingProduct.Price,
            Description = command.Description ?? existingProduct.Description,
            UpdatedAt = DateTime.UtcNow
        };

        _products[command.ProductId] = updatedProduct;

        await Task.CompletedTask; // Simulate async work

        return (updatedProduct, new ProductUpdated(command.ProductId, updatedProduct.Name, updatedProduct.Price, DateTime.UtcNow));
    }

    /// <summary>
    /// Deletes a product
    /// </summary>
    public async Task<(Result, ProductDeleted?)> HandleAsync(DeleteProduct command)
    {
        if (!_products.Remove(command.ProductId, out _))
            return (Result.NotFound($"Product {command.ProductId} not found"), null);

        await Task.CompletedTask; // Simulate async work

        return (Result.Success(), new ProductDeleted(command.ProductId, DateTime.UtcNow));
    }

    /// <summary>
    /// Handles entity actions for products
    /// </summary>
    public async Task<Result> HandleAsync(EntityAction<Product> command, ILogger<ProductHandler> logger)
    {
        logger.LogInformation("Handling entity action {Action} for product {ProductId}: {TypeName}", command.Action, command.Entity.Id, MessageTypeKey.Get(typeof(EntityAction<Product>)));
        await Task.CompletedTask; // Simulate async work

        return Result.Success();
    }
}
