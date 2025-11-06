using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests;

public class GenericMessageTests : GeneratorTestBase
{
    [Fact]
    public void GeneratesDistinctWrappersForClosedGenericMessages()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage<T>(T Value) : IQuery;

            public class IntHandler {
                public Task<int> HandleAsync(MyMessage<int> message, CancellationToken ct) => Task.FromResult(message.Value + 1);
            }

            public class StringHandler {
                public Task<string> HandleAsync(MyMessage<string> message, CancellationToken ct) => Task.FromResult(message.Value + "!");
            }
            """;

        var (compilation, diagnostics, generatedTrees) = RunGenerator(source, new[] { new MediatorGenerator() });

        Assert.Empty(diagnostics);

        // Expect two handler wrapper files with unique message identifiers including generic argument
        var intWrapper = generatedTrees.FirstOrDefault(t => t.HintName.Contains("IntHandler") && t.HintName.Contains("MyMessage"));
        var stringWrapper = generatedTrees.FirstOrDefault(t => t.HintName.Contains("StringHandler") && t.HintName.Contains("MyMessage"));

        Assert.True(intWrapper != default, "No handler wrapper generated for IntHandler");
        Assert.True(stringWrapper != default, "No handler wrapper generated for StringHandler");

        Assert.NotEqual(intWrapper.HintName, stringWrapper.HintName); // should differ due to generic argument identifiers

        // Ensure the identifiers include the generic argument name to avoid collisions
        Assert.Contains("MyMessage_Int32", intWrapper.HintName);
        Assert.Contains("MyMessage_String", stringWrapper.HintName);
    }

    [Fact]
    public async Task CanInvokeClosedGenericMessageHandlersAtRuntime()
    {
        // Define messages and handlers in-line for runtime integration
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<IntHandler>()); // will scan assembly for both handlers

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var intResult = await mediator.InvokeAsync<int>(new MyMessage<int>(40));
        Assert.Equal(42, intResult);

        var stringResult = await mediator.InvokeAsync<string>(new MyMessage<string>("Hello"));
        Assert.Equal("Hello?", stringResult);
    }

    [Fact]
    public void RegistersFriendlyGenericKey()
    {
        var source = """
             using System.Threading;
             using System.Threading.Tasks;
             using Foundatio.Mediator;

             public record Order(string Id);
             public record EntityAction<T>(T Entity) : IQuery;

             public class SomeBaseClass<T> { }

             public class OrderActionHandler : SomeBaseClass<Order> {
                 public Task<Result> HandleAsync(EntityAction<Order> action, CancellationToken ct) => Task.FromResult(Result.Success());
             }
             """;

        var (compilation, diagnostics, trees) = RunGenerator(source, [ new MediatorGenerator() ]);
        Assert.Empty(diagnostics);

        var di = trees.First(t => t.HintName.EndsWith("_MediatorHandlers.g.cs"));
        // Should use helper call and not raw backtick notation
        Assert.Contains("MessageTypeKey.Get(typeof(EntityAction<Order>))", di.Source);
        Assert.DoesNotContain("EntityAction`1[[", di.Source);
    }

    // Test types
    public record MyMessage<T>(T Value) : IQuery;
    public class IntHandler { public Task<int> HandleAsync(MyMessage<int> message, CancellationToken ct) => Task.FromResult(message.Value + 2); }
    public class StringHandler { public Task<string> HandleAsync(MyMessage<string> message, CancellationToken ct) => Task.FromResult(message.Value + "?"); }
}
