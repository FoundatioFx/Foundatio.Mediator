namespace Foundatio.Mediator.Tests;

public class AttributeHandlerGenerationTests : GeneratorTestBase
{
    [Fact]
    public async Task GeneratesWrapperForClassWithHandlerAttribute()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message) : IQuery<string>;

            [FoundatioHandler]
            public class PingProcessor {
                public Task<string> HandleAsync(Ping message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }

    [Fact]
    public async Task GeneratesWrapperForMethodWithHandlerAttribute()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message) : IQuery<string>;

            public class SomeClass {
                [FoundatioHandler]
                public Task<string> SomeMethodAsync(Ping message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}
