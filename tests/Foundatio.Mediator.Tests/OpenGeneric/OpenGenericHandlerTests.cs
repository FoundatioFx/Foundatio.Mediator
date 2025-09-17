using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Foundatio.Mediator.Tests.OpenGeneric;

public record UpdateEntity<T>(T Entity) : ICommand;
public record UpdateEntityPair<T1, T2>(T1 First, T2 Second) : ICommand;

public class EntityHandler<T>
{
    public Task HandleAsync(UpdateEntity<T> command, CancellationToken cancellationToken)
    {
        // no-op
        return Task.CompletedTask;
    }
}

public class EntityPairHandler<T1, T2>
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

        await mediator.InvokeAsync(new UpdateEntity<int>(5));
    }

    [Fact]
    public async Task CanInvokeTwoGenericParameterHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.InvokeAsync(new UpdateEntityPair<int, string>(5, "test"));
    }
}
