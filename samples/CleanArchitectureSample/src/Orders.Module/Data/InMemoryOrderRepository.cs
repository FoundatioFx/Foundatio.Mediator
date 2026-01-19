using System.Collections.Concurrent;
using Orders.Module.Domain;

namespace Orders.Module.Data;

/// <summary>
/// In-memory implementation of IOrderRepository for demonstration purposes.
/// In a real application, this would be replaced with EF Core, Dapper, or another data access technology.
/// The handler code remains unchanged because it depends on the IOrderRepository abstraction.
/// </summary>
public class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<string, Order> _orders = new(GetSeedData().ToDictionary(o => o.Id));

    public Task<Order?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Order>>(_orders.Values.ToList());
    }

    public Task<IReadOnlyList<Order>> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var orders = _orders.Values
            .Where(o => o.CustomerId == customerId)
            .ToList();
        return Task.FromResult<IReadOnlyList<Order>>(orders);
    }

    public Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_orders.TryRemove(id, out _));
    }

    private static IEnumerable<Order> GetSeedData()
    {
        var baseDate = DateTime.UtcNow.AddDays(-30);

        yield return new Order(
            Id: "ord-001-alice-laptop",
            CustomerId: "cust-alice",
            Amount: 1299.99m,
            Description: "MacBook Pro 14-inch",
            Status: OrderStatus.Delivered,
            CreatedAt: baseDate,
            UpdatedAt: baseDate.AddDays(5));

        yield return new Order(
            Id: "ord-002-alice-accessories",
            CustomerId: "cust-alice",
            Amount: 149.99m,
            Description: "Wireless keyboard and mouse combo",
            Status: OrderStatus.Delivered,
            CreatedAt: baseDate.AddDays(2),
            UpdatedAt: baseDate.AddDays(6));

        yield return new Order(
            Id: "ord-003-bob-monitor",
            CustomerId: "cust-bob",
            Amount: 549.99m,
            Description: "27-inch 4K Monitor",
            Status: OrderStatus.Shipped,
            CreatedAt: baseDate.AddDays(10),
            UpdatedAt: baseDate.AddDays(12));

        yield return new Order(
            Id: "ord-004-charlie-headset",
            CustomerId: "cust-charlie",
            Amount: 299.99m,
            Description: "Premium noise-canceling headset",
            Status: OrderStatus.Processing,
            CreatedAt: baseDate.AddDays(20));

        yield return new Order(
            Id: "ord-005-charlie-webcam",
            CustomerId: "cust-charlie",
            Amount: 179.99m,
            Description: "4K Webcam with ring light",
            Status: OrderStatus.Confirmed,
            CreatedAt: baseDate.AddDays(25));

        yield return new Order(
            Id: "ord-006-diana-desk",
            CustomerId: "cust-diana",
            Amount: 899.99m,
            Description: "Standing desk with motorized adjustment",
            Status: OrderStatus.Pending,
            CreatedAt: baseDate.AddDays(28));

        yield return new Order(
            Id: "ord-007-bob-cables",
            CustomerId: "cust-bob",
            Amount: 45.99m,
            Description: "USB-C cable bundle (5-pack)",
            Status: OrderStatus.Delivered,
            CreatedAt: baseDate.AddDays(15),
            UpdatedAt: baseDate.AddDays(18));
    }
}
