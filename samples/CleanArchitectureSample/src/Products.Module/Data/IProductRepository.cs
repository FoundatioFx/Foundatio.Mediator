using Products.Module.Domain;

namespace Products.Module.Data;

/// <summary>
/// Repository interface for Product persistence.
/// Following Clean Architecture, handlers depend on this abstraction,
/// not on concrete implementations like databases or in-memory stores.
/// </summary>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
