namespace Foundatio.Mediator.Tests;

public class MarkerInterfaceHandlerGenerationTests : GeneratorTestBase
{
    [Fact]
    public async Task GeneratesWrapperForMarkerInterfaceHandler()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message) : IQuery<string>;

            public class PingProcessor : IFoundatioHandler {
                public Task<string> HandleAsync(Ping message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}
