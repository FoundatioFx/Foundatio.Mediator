using System.ComponentModel.DataAnnotations;

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
    decimal Price);

public record UpdateProduct(
    [Required] string ProductId,
    string? Name,
    string? Description,
    decimal? Price);

public record DeleteProduct([Required] string ProductId);

// Queries
public record GetProduct([Required] string ProductId);
public record GetProducts();

// Events
public record ProductCreated(string ProductId, string Name, decimal Price, DateTime CreatedAt);
public record ProductUpdated(string ProductId, string Name, decimal Price, DateTime UpdatedAt);
public record ProductDeleted(string ProductId, DateTime DeletedAt);

// Models
public record Product(
    string Id,
    string Name,
    decimal Price,
    string Description,
    DateTime CreatedAt,
    DateTime? UpdatedAt = null);
