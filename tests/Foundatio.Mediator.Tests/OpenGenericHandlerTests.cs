using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests;

public record Order();
public record UpdateEntity<T>(T Entity) : ICommand;
public record UpdateEntityPair<T1, T2>(T1 First, T2 Second) : ICommand;

public class EntityHandlerBase<T1, T2> where T1 : class where T2 : class
{
    public Task HandlesAsync(UpdateEntity<T1> command, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public class EntityHandler<T> : EntityHandlerBase<T, T> where T : class
{
}

public class EntityPairHandler<T1, T2>
    where T1 : class
    where T2 : class
{
    public Task HandleAsync(UpdateEntityPair<T1, T2> command, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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
