using Orders.Module.Domain;

namespace Orders.Module.Data;

/// <summary>
/// Repository interface for Order persistence.
/// Following Clean Architecture, handlers depend on this abstraction,
/// not on concrete implementations like databases or in-memory stores.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
