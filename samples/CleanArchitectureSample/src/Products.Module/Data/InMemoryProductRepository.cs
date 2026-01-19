using System.Collections.Concurrent;
using Products.Module.Domain;

namespace Products.Module.Data;

/// <summary>
/// In-memory implementation of IProductRepository for demonstration purposes.
/// In a real application, this would be replaced with EF Core, Dapper, or another data access technology.
/// The handler code remains unchanged because it depends on the IProductRepository abstraction.
/// </summary>
public class InMemoryProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<string, Product> _products = new(GetSeedData().ToDictionary(p => p.Id));

    public Task<Product?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _products.TryGetValue(id, out var product);
        return Task.FromResult(product);
    }

    public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Product>>(_products.Values.ToList());
    }

    public Task<IReadOnlyList<Product>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        var normalizedSearch = searchTerm.ToLowerInvariant();
        var products = _products.Values
            .Where(p => p.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<Product>>(products);
    }

    public Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        _products[product.Id] = product;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        _products[product.Id] = product;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_products.TryRemove(id, out _));
    }

    private static IEnumerable<Product> GetSeedData()
    {
        var baseDate = DateTime.UtcNow.AddDays(-60);

        yield return new Product(
            Id: "prod-001-laptop",
            Name: "MacBook Pro 14-inch",
            Description: "Apple M3 Pro chip, 18GB RAM, 512GB SSD, Space Gray",
            Price: 1299.99m,
            StockQuantity: 25,
            Status: ProductStatus.Active,
            CreatedAt: baseDate);

        yield return new Product(
            Id: "prod-002-keyboard",
            Name: "Wireless Ergonomic Keyboard",
            Description: "Bluetooth mechanical keyboard with backlit keys and wrist rest",
            Price: 89.99m,
            StockQuantity: 150,
            Status: ProductStatus.Active,
            CreatedAt: baseDate.AddDays(5));

        yield return new Product(
            Id: "prod-003-mouse",
            Name: "Gaming Mouse Pro",
            Description: "High-precision gaming mouse with RGB lighting and 8 programmable buttons",
            Price: 59.99m,
            StockQuantity: 200,
            Status: ProductStatus.Active,
            CreatedAt: baseDate.AddDays(5));

        yield return new Product(
            Id: "prod-004-monitor",
            Name: "27-inch 4K Monitor",
            Description: "Ultra HD IPS display with USB-C connectivity and built-in speakers",
            Price: 549.99m,
            StockQuantity: 40,
            Status: ProductStatus.Active,
            CreatedAt: baseDate.AddDays(10));

        yield return new Product(
            Id: "prod-005-headset",
            Name: "Premium Noise-Canceling Headset",
            Description: "Wireless headset with active noise cancellation and 30-hour battery life",
            Price: 299.99m,
            StockQuantity: 8,  // Low stock - should trigger alerts
            Status: ProductStatus.Active,
            CreatedAt: baseDate.AddDays(15));

        yield return new Product(
            Id: "prod-006-webcam",
            Name: "4K Webcam with Ring Light",
            Description: "Professional streaming webcam with built-in ring light and autofocus",
            Price: 179.99m,
            StockQuantity: 75,
            Status: ProductStatus.Active,
            CreatedAt: baseDate.AddDays(20));

        yield return new Product(
            Id: "prod-007-desk",
            Name: "Standing Desk Pro",
            Description: "Motorized height-adjustable desk with memory presets and cable management",
            Price: 899.99m,
            StockQuantity: 15,
            Status: ProductStatus.Active,
            CreatedAt: baseDate.AddDays(25));

        yield return new Product(
            Id: "prod-008-cables",
            Name: "USB-C Cable Bundle",
            Description: "5-pack of braided USB-C cables in various lengths (1ft, 3ft, 6ft)",
            Price: 45.99m,
            StockQuantity: 500,
            Status: ProductStatus.Active,
            CreatedAt: baseDate.AddDays(30));

        yield return new Product(
            Id: "prod-009-docking",
            Name: "USB-C Docking Station",
            Description: "12-in-1 docking station with dual HDMI, ethernet, and 100W power delivery",
            Price: 189.99m,
            StockQuantity: 3,  // Very low stock
            Status: ProductStatus.Active,
            CreatedAt: baseDate.AddDays(35));

        yield return new Product(
            Id: "prod-010-chair",
            Name: "Ergonomic Office Chair",
            Description: "Mesh back office chair with lumbar support and adjustable armrests",
            Price: 449.99m,
            StockQuantity: 0,  // Out of stock
            Status: ProductStatus.OutOfStock,
            CreatedAt: baseDate.AddDays(40));

        yield return new Product(
            Id: "prod-011-old-keyboard",
            Name: "Classic Wired Keyboard",
            Description: "Basic USB keyboard - being phased out",
            Price: 19.99m,
            StockQuantity: 12,
            Status: ProductStatus.Discontinued,
            CreatedAt: baseDate.AddDays(-100));
    }
}
