namespace Products.Module.Versions.V2025_06_01;

/// <summary>
/// Simplified product representation — excludes internal fields like Status and StockQuantity.
/// </summary>
public record ProductDto(string Id, string Name, decimal Price);
