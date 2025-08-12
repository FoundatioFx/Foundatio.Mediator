using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_InvokeAsyncTests
{
    public record E2ePing(string Message) : IQuery;

    public class E2ePingHandler
    {
        public Task<string> HandleAsync(E2ePing message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
    }

    [Fact]
    public async Task InvokeAsync_ReturnsExpected()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<E2ePingHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.InvokeAsync<string>(new E2ePing("Ping"));
        result.Should().Be("Ping Pong");
    }
}
