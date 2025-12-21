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
        var di = trees.First(t => t.HintName == "_FoundatioModule.cs");
        Assert.Contains("MessageTypeKey.Get(typeof(A))", di.Source);
        Assert.Contains("MessageTypeKey.Get(typeof(B))", di.Source);
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

    [Fact]
    public void DefaultSingleton_DoesNotRegisterHandlers()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record A;
            public class AHandler { public Task HandleAsync(A m, CancellationToken ct) => Task.CompletedTask; }
            """;

        var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);
        var di = trees.First(t => t.HintName == "_FoundatioModule.cs");
        Assert.DoesNotContain("AddScoped<AHandler>()", di.Source);
        Assert.DoesNotContain("AddTransient<AHandler>()", di.Source);
    }

    [Fact]
    public void Handles_MultipleMessages()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record A;
            public record B;
            public class MultiHandler {
                public Task HandleAsync(A m, CancellationToken ct) => Task.CompletedTask;
                public Task HandleAsync(B m, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);
        var di = trees.First(t => t.HintName == "_FoundatioModule.cs");
        Assert.Contains("MessageTypeKey.Get(typeof(A))", di.Source);
        Assert.Contains("MessageTypeKey.Get(typeof(B))", di.Source);
        Assert.Contains("MultiHandler_A_Handler", di.Source);
        Assert.Contains("MultiHandler_B_Handler", di.Source);
    }

    [Theory]
    [InlineData("Transient", "AddTransient<AHandler>()")]
    [InlineData("Scoped", "AddScoped<AHandler>()")]
    [InlineData("Singleton", "AddSingleton<AHandler>()")]
    public void RegistersHandlers_WhenConfigured(string lifetime, string expected)
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record A;
            public class AHandler { public Task HandleAsync(A m, CancellationToken ct) => Task.CompletedTask; }
            """;

        var opts = CreateOptions(("build_property.MediatorHandlerLifetime", lifetime));
        var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ], opts);
        var di = trees.First(t => t.HintName == "_FoundatioModule.cs");
        Assert.Contains(expected, di.Source);
    }

    [Fact]
    public void DoesNotRegister_StaticHandlers()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record A;
            public class AHandler { public static Task HandleAsync(A m, CancellationToken ct) => Task.CompletedTask; }
            """;

        var opts = CreateOptions(("build_property.MediatorHandlerLifetime", "Transient"));
        var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ], opts);
        var di = trees.First(t => t.HintName == "_FoundatioModule.cs");
        Assert.DoesNotContain("AddTransient<AHandler>()", di.Source);
    }
}
