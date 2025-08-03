namespace Foundatio.Mediator.Benchmarks.Services;

public record Order(int Id, decimal Amount, DateTime Date);

public interface IOrderService
{
    Task<Order> GetOrderAsync(int id, CancellationToken cancellationToken = default);
}

public class OrderService : IOrderService
{
    public async Task<Order> GetOrderAsync(int id, CancellationToken cancellationToken = default)
    {
        // Simulate minimal async work
        await Task.CompletedTask;
        return new Order(id, 99.99m, DateTime.UtcNow);
    }
}
