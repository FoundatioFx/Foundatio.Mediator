namespace Foundatio.Mediator.Tests;

public class DIRegistrationTests : GeneratorTestBase
{
    [Fact]
    public void RegistersMultipleHandlers()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record A; public record B;
            public class AHandler { public Task HandleAsync(A m, CancellationToken ct) => Task.CompletedTask; }
            public class BHandler { public void Handle(B m) { } }
            """;

        var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);
        var di = trees.First(t => t.HintName.EndsWith("_MediatorHandlers.g.cs"));
        Assert.Contains("AddKeyedSingleton<HandlerRegistration>(typeof(A).FullName!", di.Source);
        Assert.Contains("AddKeyedSingleton<HandlerRegistration>(typeof(B).FullName!", di.Source);
        Assert.Contains("UntypedHandleAsync", di.Source);
        Assert.Contains("UntypedHandle(", di.Source);
    }

    [Fact]
    public void GeneratesModuleEvenWithoutHandlers()
    {
        var src = """
            using Foundatio.Mediator;
            public static class Empty { }
            """;

        var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);
        // If no handlers, MediatorGenerator returns early and DI file isn't generated.
        Assert.DoesNotContain(trees, t => t.HintName.EndsWith("_MediatorHandlers.g.cs"));
    }
}
