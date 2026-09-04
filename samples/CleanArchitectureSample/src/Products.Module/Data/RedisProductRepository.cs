using System.Text.Json;
using Products.Module.Domain;
using StackExchange.Redis;

namespace Products.Module.Data;

/// <summary>
/// Redis-backed implementation of <see cref="IProductRepository"/>.
/// Uses Redis hashes for individual products and a set to track all product IDs.
/// Shared across all API replicas so writes on one node are immediately
/// visible to reads on every other node.
/// </summary>
public class RedisProductRepository : IProductRepository
{
    private const string HashPrefix = "product:";
    private const string IndexKey = "products:index";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IConnectionMultiplexer _redis;

    public RedisProductRepository(IConnectionMultiplexer redis)
    {
        _redis = redis;
        SeedIfEmptyAsync().GetAwaiter().GetResult();
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task<Product?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var json = await Db.StringGetAsync(HashPrefix + id).ConfigureAwait(false);
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<Product>((string)json!, JsonOptions);
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var ids = await Db.SetMembersAsync(IndexKey).ConfigureAwait(false);
        if (ids.Length == 0)
            return [];

        var keys = ids.Select(id => (RedisKey)(HashPrefix + (string)id!)).ToArray();
        var values = await Db.StringGetAsync(keys).ConfigureAwait(false);

        var products = new List<Product>(values.Length);
        foreach (var v in values)
        {
            if (!v.IsNullOrEmpty)
                products.Add(JsonSerializer.Deserialize<Product>((string)v!, JsonOptions)!);
        }

        return products;
    }

    public async Task<IReadOnlyList<Product>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(p =>
            p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            p.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(product, JsonOptions);
        var batch = Db.CreateBatch();
        _ = batch.StringSetAsync(HashPrefix + product.Id, json);
        _ = batch.SetAddAsync(IndexKey, product.Id);
        batch.Execute();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(product, JsonOptions);
        await Db.StringSetAsync(HashPrefix + product.Id, json).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existed = await Db.KeyDeleteAsync(HashPrefix + id).ConfigureAwait(false);
        await Db.SetRemoveAsync(IndexKey, id).ConfigureAwait(false);
        return existed;
    }

    private async Task SeedIfEmptyAsync()
    {
        // Only seed if the index is empty (first node to start)
        if (await Db.SetLengthAsync(IndexKey).ConfigureAwait(false) > 0)
            return;

        var baseDate = DateTime.UtcNow.AddDays(-60);
        var seedProducts = new[]
        {
            new Product("prod-001-laptop", "MacBook Pro 14-inch",
                "Apple M3 Pro chip, 18GB RAM, 512GB SSD, Space Gray",
                1299.99m, 25, ProductStatus.Active, baseDate),
            new Product("prod-002-keyboard", "Wireless Ergonomic Keyboard",
                "Bluetooth mechanical keyboard with backlit keys and wrist rest",
                89.99m, 150, ProductStatus.Active, baseDate.AddDays(5)),
            new Product("prod-003-mouse", "Gaming Mouse Pro",
                "High-precision gaming mouse with RGB lighting and 8 programmable buttons",
                59.99m, 200, ProductStatus.Active, baseDate.AddDays(5)),
            new Product("prod-004-monitor", "27-inch 4K Monitor",
                "Ultra HD IPS display with USB-C connectivity and built-in speakers",
                549.99m, 40, ProductStatus.Active, baseDate.AddDays(10)),
            new Product("prod-005-headset", "Premium Noise-Canceling Headset",
                "Wireless headset with active noise cancellation and 30-hour battery life",
                299.99m, 8, ProductStatus.Active, baseDate.AddDays(15)),
            new Product("prod-006-webcam", "4K Webcam with Ring Light",
                "Professional streaming webcam with built-in ring light and autofocus",
                179.99m, 75, ProductStatus.Active, baseDate.AddDays(20)),
            new Product("prod-007-desk", "Standing Desk Pro",
                "Motorized height-adjustable desk with memory presets and cable management",
                899.99m, 15, ProductStatus.Active, baseDate.AddDays(25)),
            new Product("prod-008-cables", "USB-C Cable Bundle",
                "5-pack of braided USB-C cables in various lengths (1ft, 3ft, 6ft)",
                45.99m, 500, ProductStatus.Active, baseDate.AddDays(30)),
            new Product("prod-009-docking", "USB-C Docking Station",
                "12-in-1 docking station with dual HDMI, ethernet, and 100W power delivery",
                189.99m, 3, ProductStatus.Active, baseDate.AddDays(35)),
            new Product("prod-010-chair", "Ergonomic Office Chair",
                "Mesh back office chair with lumbar support and adjustable armrests",
                449.99m, 0, ProductStatus.OutOfStock, baseDate.AddDays(40)),
            new Product("prod-011-old-keyboard", "Classic Wired Keyboard",
                "Basic USB keyboard - being phased out",
                19.99m, 12, ProductStatus.Discontinued, baseDate.AddDays(-100))
        };

        foreach (var product in seedProducts)
            await AddAsync(product).ConfigureAwait(false);
    }
}
