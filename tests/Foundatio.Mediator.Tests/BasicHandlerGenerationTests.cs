namespace Foundatio.Mediator.Tests;

public class BasicHandlerGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public async Task GeneratesWrapperForSimpleHandler()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message) : IQuery<string>;

            public class PingHandler {
                public Task<string> HandleAsync(Ping message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}
