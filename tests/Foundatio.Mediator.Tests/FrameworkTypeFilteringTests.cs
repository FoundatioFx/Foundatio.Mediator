namespace Foundatio.Mediator.Tests;

/// <summary>
/// Tests that the generator produces valid (compilable) code for any handler it discovers,
/// regardless of return type or parameter types. The generator should never emit code that
/// causes CS0266 or other compilation errors.
/// </summary>
public class GeneratedCodeValidityTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    /// <summary>
    /// Handler returning a concrete collection type (List&lt;T&gt;) should produce valid code.
    /// This is the pattern that previously caused CS0266 in Execute middleware pipelines.
    /// </summary>
    [Fact]
    public void Handler_ReturnsListOfT_GeneratesValidCode()
    {
        var src = """
            using System.Collections.Generic;
            using Foundatio.Mediator;

            public record GetItems(string Category);

            public class GetItemsHandler
            {
                public List<string> Handle(GetItems query) => new List<string> { "a", "b" };
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.Contains(trees, t => t.HintName.Contains("GetItemsHandler"));
    }

    /// <summary>
    /// Handler returning an interface type should produce valid code.
    /// </summary>
    [Fact]
    public void Handler_ReturnsInterfaceType_GeneratesValidCode()
    {
        var src = """
            using System.Collections.Generic;
            using Foundatio.Mediator;

            public record GetData(int Id);

            public class GetDataHandler
            {
                public IReadOnlyList<string> Handle(GetData query) => new[] { "data" };
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.Contains(trees, t => t.HintName.Contains("GetDataHandler"));
    }

    /// <summary>
    /// Handler returning a concrete collection with Execute middleware should produce valid code.
    /// This is the exact pattern that caused CS0266 — the Execute middleware wraps the pipeline
    /// in a delegate returning object?, and the result must be cast back.
    /// </summary>
    [Fact]
    public void Handler_ReturnsListOfT_WithExecuteMiddleware_GeneratesValidCode()
    {
        var src = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record GetItems(string Category);

            public class GetItemsHandler
            {
                public List<string> Handle(GetItems query) => new List<string> { "a", "b" };
            }

            public static class RetryMiddleware
            {
                public static async ValueTask<object?> ExecuteAsync(object message, HandlerExecutionDelegate next)
                {
                    return await next();
                }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.Contains(trees, t => t.HintName.Contains("GetItemsHandler"));
    }

    /// <summary>
    /// Convention-discovered handler that takes a complex generic wrapper as the first
    /// parameter (like MassTransit's ConsumeContext&lt;T&gt;) should produce valid code.
    /// </summary>
    [Fact]
    public void ConventionHandler_WithComplexGenericParam_GeneratesValidCode()
    {
        var src = """
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            namespace SomeLib
            {
                public interface ConsumeContext<T> { T Message { get; } }
            }

            public record ProcessRefund(string Id);

            public class ProcessRefundConsumer
            {
                public Task Consume(SomeLib.ConsumeContext<ProcessRefund> context)
                    => Task.CompletedTask;
            }
            """;

        // Should either skip or produce valid code — never CS0266
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        // We don't assert whether it's included or excluded — only that the compilation is clean
        // (AssertNoCompilationDiagnostics is called by RunGenerator)
    }

    /// <summary>
    /// Convention-discovered handler returning a value type (int) should produce valid code.
    /// </summary>
    [Fact]
    public void Handler_ReturnsValueType_GeneratesValidCode()
    {
        var src = """
            using Foundatio.Mediator;

            public record CountItems(string Category);

            public class CountItemsHandler
            {
                public int Handle(CountItems query) => 42;
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.Contains(trees, t => t.HintName.Contains("CountItemsHandler"));
    }

    /// <summary>
    /// Handler returning a value type with Execute middleware should produce valid code.
    /// Value types require unboxing from object? which could fail differently than reference types.
    /// </summary>
    [Fact]
    public void Handler_ReturnsValueType_WithExecuteMiddleware_GeneratesValidCode()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record CountItems(string Category);

            public class CountItemsHandler
            {
                public int Handle(CountItems query) => 42;
            }

            public static class GlobalMiddleware
            {
                public static async ValueTask<object?> ExecuteAsync(object message, HandlerExecutionDelegate next)
                {
                    return await next();
                }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.Contains(trees, t => t.HintName.Contains("CountItemsHandler"));
    }

    /// <summary>
    /// Multiple handlers in the same project — one legitimate, one convention-matched from
    /// a non-Foundatio pattern — should all produce valid code.
    /// </summary>
    [Fact]
    public void MixedHandlers_AllGenerateValidCode()
    {
        var src = """
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            namespace SomeLib
            {
                public interface ConsumeContext<T> { T Message { get; } }
            }

            public record GetOrder(string Id);
            public record ProcessRefund(string Id);

            public class OrderHandler
            {
                public string Handle(GetOrder query) => query.Id;
            }

            public class RefundConsumer
            {
                public Task Consume(SomeLib.ConsumeContext<ProcessRefund> context)
                    => Task.CompletedTask;
            }
            """;

        // Both should compile cleanly
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.Contains(trees, t => t.HintName.Contains("OrderHandler"));
    }
}
