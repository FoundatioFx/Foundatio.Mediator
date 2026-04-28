using System.ComponentModel.DataAnnotations;
using Foundatio.Mediator;
using Products.Module.Domain;

namespace Products.Module.Messages;

// Commands
public record CreateProduct(
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Name must be between 3 and 100 characters")]
    string Name,

    [Required(ErrorMessage = "Description is required")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "Description must be between 5 and 500 characters")]
    string Description,

    [Required(ErrorMessage = "Price is required")]
    [Range(0.01, 1000000, ErrorMessage = "Price must be between $0.01 and $1,000,000")]
    decimal Price,

    [Range(0, 1000000, ErrorMessage = "Stock quantity must be between 0 and 1,000,000")]
    int StockQuantity = 0) : ICommand<Result<Product>>;

public record UpdateProduct(
    [Required] string ProductId,
    string? Name,
    string? Description,
    decimal? Price,
    int? StockQuantity,
    ProductStatus? Status) : ICommand<Result<Product>>;

public record DeleteProduct([Required] string ProductId) : ICommand<Result>;

// Queries
public record GetProduct([Required] string ProductId) : IQuery<Result<Product>>;

public record GetProducts() : IQuery<Result<List<Product>>>;

/// <summary>
/// Returns an aggregated product catalog summary.
/// The handler simulates an expensive computation (500ms delay) that is cached.
/// First call is slow; subsequent calls return instantly from cache.
/// </summary>
public record GetProductCatalog() : IQuery<Result<ProductCatalogSummary>>;

/// <summary>
/// Gets a specific product review by its unique identifier.
/// Demonstrates typed route constraints — <c>Guid ReviewId</c> generates <c>{reviewId:guid}</c> in the route.
/// </summary>
public record GetProductReview(string ProductId, Guid ReviewId) : IQuery<Result<ProductReview>>;

/// <summary>
/// A product review with a <see cref="Guid"/> identifier, demonstrating typed route parameter constraints.
/// </summary>
public record ProductReview(Guid ReviewId, string ProductId, string Author, string Content, int Rating, DateTime CreatedAt);

/// <summary>
/// Aggregated catalog statistics returned by <see cref="GetProductCatalog"/>.
/// </summary>
public record ProductCatalogSummary(
    int TotalProducts,
    int ActiveProducts,
    decimal AveragePrice,
    DateTime GeneratedAt);
