using Common.Module;
using Foundatio.Mediator;
using Products.Module.Data;
using Products.Module.Domain;

namespace Products.Module.Versions.V2025_06_01;

/// <summary>
/// 2025-06-01 product endpoints — returns a simplified product DTO without internal status fields.
/// Demonstrates date-based API versioning: same "Products" group, different API version.
/// </summary>
[HandlerEndpointGroup("Products", ApiVersion = ApiConstants.V2)]
public class ProductHandler(IProductRepository repository)
{
    /// <summary>
    /// (2025-06-01) Returns a simplified product DTO.
    /// </summary>
    [HandlerAllowAnonymous]
    [HandlerEndpoint(Route = "{productId}")]
    public async Task<Result<ProductDto>> HandleAsync(GetProduct query, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
            return Result.NotFound($"Product {query.ProductId} not found");

        return new ProductDto(product.Id, product.Name, product.Price);
    }

    /// <summary>
    /// (2025-06-01) Returns simplified product list.
    /// </summary>
    [HandlerAllowAnonymous]
    [HandlerEndpoint(Route = "")]
    public async Task<Result<List<ProductDto>>> HandleAsync(GetProducts query, CancellationToken cancellationToken)
    {
        var products = await repository.GetAllAsync(cancellationToken);
        return products.Select(p => new ProductDto(p.Id, p.Name, p.Price)).ToList();
    }
}
