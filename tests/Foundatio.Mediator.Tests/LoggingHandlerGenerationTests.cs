namespace Foundatio.Mediator.Tests;

public class LoggingHandlerGenerationTests : GeneratorTestBase
{
    [Fact]
    public async Task GeneratedHandlerIncludesLoggingStatements()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record TestMessage(string Content);

            public class TestHandler 
            {
                public void Handle(TestMessage message) 
                {
                    // Handler logic
                }
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }

    [Fact]
    public async Task GeneratedAsyncHandlerIncludesLoggingStatements()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record AsyncTestMessage(string Content);

            public class AsyncTestHandler 
            {
                public async Task HandleAsync(AsyncTestMessage message, CancellationToken cancellationToken) 
                {
                    await Task.CompletedTask;
                }
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}