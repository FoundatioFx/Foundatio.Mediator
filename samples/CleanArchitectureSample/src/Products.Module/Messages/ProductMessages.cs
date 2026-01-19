using System.ComponentModel.DataAnnotations;
using Common.Module.Middleware;
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
    int StockQuantity = 0) : IValidatable, ICommand<Result<Product>>;

public record UpdateProduct(
    [Required] string ProductId,
    string? Name,
    string? Description,
    decimal? Price,
    int? StockQuantity,
    ProductStatus? Status) : IValidatable, ICommand<Result<Product>>;

public record DeleteProduct([Required] string ProductId) : IValidatable, ICommand<Result>;

// Queries
public record GetProduct([Required] string ProductId) : IValidatable, IQuery<Result<Product>>;
public record GetProducts() : IQuery<Result<List<Product>>>;
