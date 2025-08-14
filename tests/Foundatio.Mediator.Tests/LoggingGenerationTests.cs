namespace Foundatio.Mediator.Tests;

public class LoggingGenerationTests : GeneratorTestBase
{
    [Fact]
    public async Task GeneratesLoggingForSimpleHandler()
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

    [Fact]
    public async Task GeneratesLoggingForStaticHandler()
    {
        var source = """
            using Foundatio.Mediator;

            public record Ping(string Message);

            public static class PingHandler {
                public static void Handle(Ping message) { }
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}