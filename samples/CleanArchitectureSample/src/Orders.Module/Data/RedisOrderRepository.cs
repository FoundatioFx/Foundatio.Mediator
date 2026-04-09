using System.Text.Json;
using Orders.Module.Domain;
using StackExchange.Redis;

using Order = Orders.Module.Domain.Order;

namespace Orders.Module.Data;

/// <summary>
/// Redis-backed implementation of <see cref="IOrderRepository"/>.
/// Uses Redis strings for individual orders and a set to track all order IDs.
/// Shared across all API replicas so writes on one node are immediately
/// visible to reads on every other node.
/// </summary>
public class RedisOrderRepository : IOrderRepository
{
    private const string HashPrefix = "order:";
    private const string IndexKey = "orders:index";
    private const string CustomerIndexPrefix = "orders:customer:";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IConnectionMultiplexer _redis;

    public RedisOrderRepository(IConnectionMultiplexer redis)
    {
        _redis = redis;
        SeedIfEmptyAsync().GetAwaiter().GetResult();
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task<Order?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var json = await Db.StringGetAsync(HashPrefix + id).ConfigureAwait(false);
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<Order>((string)json!, JsonOptions);
    }

    public async Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var ids = await Db.SetMembersAsync(IndexKey).ConfigureAwait(false);
        if (ids.Length == 0)
            return [];

        var keys = ids.Select(id => (RedisKey)(HashPrefix + (string)id!)).ToArray();
        var values = await Db.StringGetAsync(keys).ConfigureAwait(false);

        var orders = new List<Order>(values.Length);
        foreach (var v in values)
        {
            if (!v.IsNullOrEmpty)
                orders.Add(JsonSerializer.Deserialize<Order>((string)v!, JsonOptions)!);
        }

        return orders;
    }

    public async Task<IReadOnlyList<Order>> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var ids = await Db.SetMembersAsync(CustomerIndexPrefix + customerId).ConfigureAwait(false);
        if (ids.Length == 0)
            return [];

        var keys = ids.Select(id => (RedisKey)(HashPrefix + (string)id!)).ToArray();
        var values = await Db.StringGetAsync(keys).ConfigureAwait(false);

        var orders = new List<Order>(values.Length);
        foreach (var v in values)
        {
            if (!v.IsNullOrEmpty)
                orders.Add(JsonSerializer.Deserialize<Order>((string)v!, JsonOptions)!);
        }

        return orders;
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(order, JsonOptions);
        var batch = Db.CreateBatch();
        _ = batch.StringSetAsync(HashPrefix + order.Id, json);
        _ = batch.SetAddAsync(IndexKey, order.Id);
        _ = batch.SetAddAsync(CustomerIndexPrefix + order.CustomerId, order.Id);
        batch.Execute();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(order, JsonOptions);
        await Db.StringSetAsync(HashPrefix + order.Id, json).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        // Read the order first to remove from customer index
        var existing = await GetByIdAsync(id).ConfigureAwait(false);
        var existed = await Db.KeyDeleteAsync(HashPrefix + id).ConfigureAwait(false);
        await Db.SetRemoveAsync(IndexKey, id).ConfigureAwait(false);
        if (existing is not null)
            await Db.SetRemoveAsync(CustomerIndexPrefix + existing.CustomerId, id).ConfigureAwait(false);
        return existed;
    }

    private async Task SeedIfEmptyAsync()
    {
        if (await Db.SetLengthAsync(IndexKey).ConfigureAwait(false) > 0)
            return;

        var baseDate = DateTime.UtcNow.AddDays(-30);
        var seedOrders = new[]
        {
            new Order("ord-001-alice-laptop", "cust-alice", 1299.99m,
                "MacBook Pro 14-inch", OrderStatus.Delivered, baseDate, baseDate.AddDays(5)),
            new Order("ord-002-alice-accessories", "cust-alice", 149.99m,
                "Wireless keyboard and mouse combo", OrderStatus.Delivered, baseDate.AddDays(2), baseDate.AddDays(6)),
            new Order("ord-003-bob-monitor", "cust-bob", 549.99m,
                "27-inch 4K Monitor", OrderStatus.Shipped, baseDate.AddDays(10), baseDate.AddDays(12)),
            new Order("ord-004-charlie-headset", "cust-charlie", 299.99m,
                "Premium noise-canceling headset", OrderStatus.Processing, baseDate.AddDays(20)),
            new Order("ord-005-charlie-webcam", "cust-charlie", 179.99m,
                "4K Webcam with ring light", OrderStatus.Confirmed, baseDate.AddDays(25)),
            new Order("ord-006-diana-desk", "cust-diana", 899.99m,
                "Standing desk with motorized adjustment", OrderStatus.Pending, baseDate.AddDays(28)),
            new Order("ord-007-bob-cables", "cust-bob", 45.99m,
                "USB-C cable bundle (5-pack)", OrderStatus.Delivered, baseDate.AddDays(15), baseDate.AddDays(18))
        };

        foreach (var order in seedOrders)
            await AddAsync(order).ConfigureAwait(false);
    }
}
