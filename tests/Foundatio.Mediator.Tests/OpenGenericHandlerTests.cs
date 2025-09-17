using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests;

public interface IEntity;
public record Order() : IEntity;
public record UpdateEntity<T>(T Entity) : ICommand;

public record UpdateEntityPair<T1, T2>(T1 First, T2 Second)
    : ICommand where T1 : class, IEntity, new();

public class EntityHandlerBase<T1> where T1 : class
{
    public Task<T1?> HandlesAsync(UpdateEntity<T1> command, CancellationToken cancellationToken)
    {
        return Task.FromResult(default(T1));
    }
}

public class EntityHandler<T1> : EntityHandlerBase<T1> where T1 : class
{
}

public class EntityPairHandler<T1, T2>
    where T1 : class, IEntity, new()
{
    public Task<T2?> HandleAsync(UpdateEntityPair<T1, T2> command, CancellationToken cancellationToken)
    {
        return Task.FromResult(default(T2));
    }
}

public class OpenGenericHandlerTests
{
    [Fact]
    public async Task CanInvokeSingleGenericParameterHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        await mediator.InvokeAsync(new UpdateEntity<Order>(new Order()));
    }

    [Fact]
    public async Task CanInvokeTwoGenericParameterHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        await mediator.InvokeAsync(new UpdateEntityPair<Order, Order>(new Order(), new Order()));
    }
}
