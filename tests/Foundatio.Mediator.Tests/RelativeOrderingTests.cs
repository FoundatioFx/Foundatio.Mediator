using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Tests;

public class RelativeOrderingTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    private static readonly MediatorGenerator Gen = new();

    [Fact]
    public void MiddlewareOrderBefore_RunsBeforeTarget()
    {
        var src = """
            using System.Threading;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

            [Middleware(OrderBefore = [typeof(MW2Middleware)])]
            public static class MW1Middleware {
                public static void Before(Msg m) { }
            }

            public static class MW2Middleware {
                public static void Before(Msg m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [Gen]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // MW1 should appear before MW2 in the generated code
        var mw1Pos = wrapper.Source.IndexOf("MW1Middleware.Before");
        var mw2Pos = wrapper.Source.IndexOf("MW2Middleware.Before");
        Assert.True(mw1Pos >= 0, "MW1Middleware.Before should appear in generated code");
        Assert.True(mw2Pos >= 0, "MW2Middleware.Before should appear in generated code");
        Assert.True(mw1Pos < mw2Pos, "MW1 should appear before MW2 since MW1 has OrderBefore = [typeof(MW2)]");
    }

    [Fact]
    public void MiddlewareOrderAfter_RunsAfterTarget()
    {
        var src = """
            using System.Threading;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

            [Middleware(OrderAfter = [typeof(MW1Middleware)])]
            public static class MW2Middleware {
                public static void Before(Msg m) { }
            }

            public static class MW1Middleware {
                public static void Before(Msg m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [Gen]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // MW1 should appear before MW2 since MW2 says "I run after MW1"
        var mw1Pos = wrapper.Source.IndexOf("MW1Middleware.Before");
        var mw2Pos = wrapper.Source.IndexOf("MW2Middleware.Before");
        Assert.True(mw1Pos >= 0, "MW1Middleware.Before should appear in generated code");
        Assert.True(mw2Pos >= 0, "MW2Middleware.Before should appear in generated code");
        Assert.True(mw1Pos < mw2Pos, "MW1 should appear before MW2 since MW2 has OrderAfter = [typeof(MW1)]");
    }

    [Fact]
    public void MiddlewareChain_OrderBefore_ThreeMiddlewares()
    {
        var src = """
            using System.Threading;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

            [Middleware(OrderBefore = [typeof(MW2Middleware)])]
            public static class MW1Middleware {
                public static void Before(Msg m) { }
            }

            [Middleware(OrderBefore = [typeof(MW3Middleware)])]
            public static class MW2Middleware {
                public static void Before(Msg m) { }
            }

            public static class MW3Middleware {
                public static void Before(Msg m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [Gen]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        var mw1Pos = wrapper.Source.IndexOf("MW1Middleware.Before");
        var mw2Pos = wrapper.Source.IndexOf("MW2Middleware.Before");
        var mw3Pos = wrapper.Source.IndexOf("MW3Middleware.Before");
        Assert.True(mw1Pos < mw2Pos, "MW1 should be before MW2");
        Assert.True(mw2Pos < mw3Pos, "MW2 should be before MW3");
    }

    [Fact]
    public void MiddlewareCycle_EmitsWarning()
    {
        var src = """
            using System.Threading;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

            [Middleware(OrderBefore = [typeof(MW2Middleware)])]
            public static class MW1Middleware {
                public static void Before(Msg m) { }
            }

            [Middleware(OrderBefore = [typeof(MW1Middleware)])]
            public static class MW2Middleware {
                public static void Before(Msg m) { }
            }
            """;

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED012" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void MiddlewareCycle_StillGeneratesCode()
    {
        var src = """
            using System.Threading;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

            [Middleware(OrderBefore = [typeof(MW2Middleware)])]
            public static class MW1Middleware {
                public static void Before(Msg m) { }
            }

            [Middleware(OrderBefore = [typeof(MW1Middleware)])]
            public static class MW2Middleware {
                public static void Before(Msg m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [Gen]);

        // Both middleware should still appear in generated code (fallback to numeric order)
        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("MW1Middleware.Before", wrapper.Source);
        Assert.Contains("MW2Middleware.Before", wrapper.Source);
    }

    [Fact]
    public void MiddlewareOrderBefore_WithNumericOrderFallback()
    {
        var src = """
            using System.Threading;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

            [Middleware(Order = 100, OrderBefore = [typeof(MW3Middleware)])]
            public static class MW1Middleware {
                public static void Before(Msg m) { }
            }

            [Middleware(Order = 50)]
            public static class MW2Middleware {
                public static void Before(Msg m) { }
            }

            [Middleware(Order = 200)]
            public static class MW3Middleware {
                public static void Before(Msg m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [Gen]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // MW2 has lowest numeric order (50) and no relative constraints, so it goes first
        // MW1 must go before MW3 due to OrderBefore constraint
        var mw1Pos = wrapper.Source.IndexOf("MW1Middleware.Before");
        var mw2Pos = wrapper.Source.IndexOf("MW2Middleware.Before");
        var mw3Pos = wrapper.Source.IndexOf("MW3Middleware.Before");
        Assert.True(mw1Pos < mw3Pos, "MW1 should appear before MW3 due to OrderBefore constraint");
    }

    [Fact]
    public void MiddlewareOrderBefore_UnknownTypeIsIgnored()
    {
        // OrderBefore references a type that doesn't exist as middleware - should be silently ignored
        var src = """
            using System.Threading;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

            public class SomeRandomClass { }

            [Middleware(OrderBefore = [typeof(SomeRandomClass)])]
            public static class MW1Middleware {
                public static void Before(Msg m) { }
            }
            """;

        var (_, genDiags, trees) = RunGenerator(src, [Gen]);

        // Should not produce cycle or error diagnostics
        Assert.DoesNotContain(genDiags, d => d.Id == "FMED012");

        // Should still generate code
        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("MW1Middleware.Before", wrapper.Source);
    }

    [Fact]
    public void HandlerOrderBefore_EmitsInRegistration()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;

            [Handler(OrderBefore = [typeof(H2Handler)])]
            public class H1Handler { public Task HandleAsync(Msg m, CancellationToken ct) => Task.CompletedTask; }

            public class H2Handler { public Task HandleAsync(Msg m, CancellationToken ct) => Task.CompletedTask; }
            """;

        var (_, _, trees) = RunGenerator(src, [Gen]);

        var module = trees.First(t => t.HintName == "_FoundatioModule.cs");
        // The generated module should contain orderBefore parameter for H1
        Assert.Contains("orderBefore:", module.Source);
        Assert.Contains("H2Handler", module.Source);
    }

    [Fact]
    public void HandlerOrderAfter_EmitsInRegistration()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;

            public class H1Handler { public Task HandleAsync(Msg m, CancellationToken ct) => Task.CompletedTask; }

            [Handler(OrderAfter = [typeof(H1Handler)])]
            public class H2Handler { public Task HandleAsync(Msg m, CancellationToken ct) => Task.CompletedTask; }
            """;

        var (_, _, trees) = RunGenerator(src, [Gen]);

        var module = trees.First(t => t.HintName == "_FoundatioModule.cs");
        // The generated module should contain orderAfter parameter for H2
        Assert.Contains("orderAfter:", module.Source);
        Assert.Contains("H1Handler", module.Source);
    }

    [Fact]
    public void MiddlewareMultipleOrderBefore_AllRespected()
    {
        var src = """
            using System.Threading;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

            [Middleware(OrderBefore = [typeof(MW2Middleware), typeof(MW3Middleware)])]
            public static class MW1Middleware {
                public static void Before(Msg m) { }
            }

            public static class MW2Middleware {
                public static void Before(Msg m) { }
            }

            public static class MW3Middleware {
                public static void Before(Msg m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [Gen]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        var mw1Pos = wrapper.Source.IndexOf("MW1Middleware.Before");
        var mw2Pos = wrapper.Source.IndexOf("MW2Middleware.Before");
        var mw3Pos = wrapper.Source.IndexOf("MW3Middleware.Before");
        Assert.True(mw1Pos < mw2Pos, "MW1 should appear before MW2");
        Assert.True(mw1Pos < mw3Pos, "MW1 should appear before MW3");
    }

    [Fact]
    public void NoRelativeOrdering_FallsBackToNumericOrder()
    {
        var src = """
            using System.Threading;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

            [Middleware(Order = 20)]
            public static class MW1Middleware {
                public static void Before(Msg m) { }
            }

            [Middleware(Order = 10)]
            public static class MW2Middleware {
                public static void Before(Msg m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [Gen]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // MW2 has Order=10, MW1 has Order=20, so MW2 should come first
        var mw1Pos = wrapper.Source.IndexOf("MW1Middleware.Before");
        var mw2Pos = wrapper.Source.IndexOf("MW2Middleware.Before");
        Assert.True(mw2Pos < mw1Pos, "MW2 (Order=10) should appear before MW1 (Order=20)");
    }
}
