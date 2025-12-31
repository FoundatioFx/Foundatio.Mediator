namespace Foundatio.Mediator.Tests;

public class HandlerConventionOverrideTests : GeneratorTestBase
{
    [Fact]
    public void GeneratesWrapperForMarkerInterfaceHandler()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message) : IQuery<string>;

            public class PingProcessor : IHandler {
                public Task<string> HandleAsync(Ping message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
            }
            """;

        var (compilation, diagnostics, generatedTrees) = RunGenerator(source, [new MediatorGenerator()]);

        // Assert no compilation or generator diagnostics
        Assert.Empty(diagnostics);


        // Assert that a handler wrapper was generated for PingProcessor
        var handlerWrapper = generatedTrees.FirstOrDefault(t => t.HintName.Contains("PingProcessor"));
        Assert.True(handlerWrapper != default, "No handler wrapper was generated for PingProcessor");
        Assert.Contains("PingProcessor_Ping_Handler", handlerWrapper.Source);

        // Assert that the generated handler contains the expected method signatures
        Assert.Contains("public static async System.Threading.Tasks.Task<string> HandleAsync", handlerWrapper.Source);
        Assert.Contains("public static async ValueTask<object?> UntypedHandleAsync", handlerWrapper.Source);

        // Assert that the handler creation logic is present
        Assert.Contains("GetOrCreateHandler", handlerWrapper.Source);
    }

    [Fact]
    public void GeneratesWrapperForClassWithHandlerAttribute()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message) : IQuery<string>;

            [Handler]
            public class PingProcessor {
                public Task<string> HandleAsync(Ping message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
            }
            """;

        var (compilation, diagnostics, generatedTrees) = RunGenerator(source, [new MediatorGenerator()]);

        // Assert no compilation or generator diagnostics
        Assert.Empty(diagnostics);


        // Assert that a handler wrapper was generated for PingProcessor
        var handlerWrapper = generatedTrees.FirstOrDefault(t => t.HintName.Contains("PingProcessor"));
        Assert.True(handlerWrapper != default, "No handler wrapper was generated for PingProcessor");
        Assert.Contains("PingProcessor_Ping_Handler", handlerWrapper.Source);

        // Assert that the generated handler contains the expected method signatures
        Assert.Contains("public static async System.Threading.Tasks.Task<string> HandleAsync", handlerWrapper.Source);
        Assert.Contains("public static async ValueTask<object?> UntypedHandleAsync", handlerWrapper.Source);

        // Assert that the handler creation logic is present
        Assert.Contains("GetOrCreateHandler", handlerWrapper.Source);
    }

    [Fact]
    public void GeneratesWrapperForMethodWithHandlerAttribute()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message) : IQuery<string>;

            public class SomeClass {
                [Handler]
                public Task<string> SomeMethodAsync(Ping message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
            }
            """;

        var (compilation, diagnostics, generatedTrees) = RunGenerator(source, [new MediatorGenerator()]);

        // Assert no compilation or generator diagnostics
        Assert.Empty(diagnostics);


        // Assert that a handler wrapper was generated for SomeClass
        var handlerWrapper = generatedTrees.FirstOrDefault(t => t.HintName.Contains("SomeClass"));
        Assert.True(handlerWrapper != default, "No handler wrapper was generated for SomeClass");
        Assert.Contains("SomeClass_Ping_Handler", handlerWrapper.Source);

        // Assert that the generated handler contains the expected method signatures
        Assert.Contains("public static async System.Threading.Tasks.Task<string> HandleAsync", handlerWrapper.Source);
        Assert.Contains("public static async ValueTask<object?> UntypedHandleAsync", handlerWrapper.Source);

        // Assert that the handler creation logic is present
        Assert.Contains("GetOrCreateHandler", handlerWrapper.Source);
    }

}

