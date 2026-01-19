using System.Text.Json.Serialization;

namespace Products.Module.Domain;

/// <summary>
/// Represents a product in the catalog.
/// This is a domain entity - the core business object for the Products bounded context.
/// </summary>
public record Product(
    string Id,
    string Name,
    string Description,
    decimal Price,
    int StockQuantity,
    ProductStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt = null);

/// <summary>
/// Represents the availability status of a product.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductStatus
{
    Draft,
    Active,
    OutOfStock,
    Discontinued
}
