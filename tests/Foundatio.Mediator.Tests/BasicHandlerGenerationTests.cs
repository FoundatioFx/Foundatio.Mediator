namespace Foundatio.Mediator.Tests;

public class BasicHandlerGenerationTests : GeneratorTestBase
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

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "true"));
        await VerifyGenerated(source, opts, new MediatorGenerator());
    }
}
