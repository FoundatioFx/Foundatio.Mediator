using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Tests;

public class CrossAssemblyMiddlewareTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void DiscoversMiddlewareFromReferencedAssembly()
    {
        // First, compile middleware in a "referenced assembly"
        var middlewareSource = """
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedMiddleware;

            [Middleware]
            public static class LoggingMiddleware
            {
                public static void Before(object message) { }
                public static void After(object message) { }
            }
            """;

        var middlewareCompilation = CreateAssembly(middlewareSource, "MiddlewareAssembly");

        // Now compile handler that should discover the middleware
        var handlerSource = """
            using System.Threading;
            using Foundatio.Mediator;

            public record TestMessage;

            public class TestHandler
            {
                public void Handle(TestMessage msg, CancellationToken ct) { }
            }
            """;

        var (compilation, _, trees) = RunGenerator(handlerSource, [new MediatorGenerator()], additionalReferences: [middlewareCompilation]);

        // Verify middleware was discovered and included in generated handler
        var wrapper = trees.FirstOrDefault(t => t.HintName.EndsWith("_Handler.g.cs"));

        // The generated handler should call the middleware from the referenced assembly
        Assert.Contains("SharedMiddleware.LoggingMiddleware.Before", wrapper.Source);
        Assert.Contains("SharedMiddleware.LoggingMiddleware.After", wrapper.Source);
    }

    [Fact]
    public void CombinesSameAssemblyAndCrossAssemblyMiddleware()
    {
        // Middleware in referenced assembly
        var externalMiddlewareSource = """
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace ExternalMiddleware;

            [Middleware(1)]
            public static class ValidationMiddleware
            {
                public static HandlerResult Before(object message) => HandlerResult.Continue();
            }
            """;

        var externalMiddlewareCompilation = CreateAssembly(externalMiddlewareSource, "MiddlewareAssembly");

        // Handler and local middleware in current assembly
        var handlerSource = """
            using System.Threading;
            using Foundatio.Mediator;

            public record TestMessage;

            public class TestHandler
            {
                public void Handle(TestMessage msg, CancellationToken ct) { }
            }

            [Middleware(2)]
            public static class LoggingMiddleware
            {
                public static void Before(object message) { }
            }
            """;

        var (compilation, _, trees) = RunGenerator(handlerSource, [new MediatorGenerator()], additionalReferences: [externalMiddlewareCompilation]);

        var wrapper = trees.FirstOrDefault(t => t.HintName.EndsWith("_Handler.g.cs"));

        // Should have both middleware
        Assert.Contains("ExternalMiddleware.ValidationMiddleware.Before", wrapper.Source);
        Assert.Contains("LoggingMiddleware.Before", wrapper.Source);

        // Validation (order 1) should come before Logging (order 2)
        var validationIndex = wrapper.Source.IndexOf("ExternalMiddleware.ValidationMiddleware.Before");
        var loggingIndex = wrapper.Source.IndexOf("LoggingMiddleware.Before");
        Assert.True(validationIndex < loggingIndex, "Validation middleware should execute before logging");
    }

    [Fact]
    public void DiscoversMiddlewareWithoutAttributeIfEndsWithMiddleware()
    {
        // Middleware without explicit [Middleware] attribute but ends with "Middleware"
        var middlewareSource = """
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace ImplicitMiddleware;

            public static class TimingMiddleware
            {
                public static void Before(object message) { }
                public static void Finally(object message, System.Exception? ex) { }
            }
            """;

        var middlewareCompilation = CreateAssembly(middlewareSource, "MiddlewareAssembly");

        var handlerSource = """
            using System.Threading;
            using Foundatio.Mediator;

            public record TestMessage;

            public class TestHandler
            {
                public void Handle(TestMessage msg, CancellationToken ct) { }
            }
            """;

        var (compilation, _, trees) = RunGenerator(handlerSource, [new MediatorGenerator()], additionalReferences: [middlewareCompilation]);

        var wrapper = trees.FirstOrDefault(t => t.HintName.EndsWith("_Handler.g.cs"));

        Assert.Contains("ImplicitMiddleware.TimingMiddleware.Before", wrapper.Source);
        Assert.Contains("ImplicitMiddleware.TimingMiddleware.Finally", wrapper.Source);
    }

    [Fact]
    public void InternalMiddlewareNotDiscoveredFromReferencedAssembly()
    {
        // Internal middleware in referenced assembly should NOT be discovered
        var middlewareSource = """
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedMiddleware;

            [Middleware]
            internal static class InternalMiddleware
            {
                public static void Before(object message) { }
            }

            [Middleware]
            public static class PublicMiddleware
            {
                public static void After(object message) { }
            }
            """;

        var middlewareCompilation = CreateAssembly(middlewareSource, "MiddlewareAssembly");

        var handlerSource = """
            using System.Threading;
            using Foundatio.Mediator;

            public record TestMessage;

            public class TestHandler
            {
                public void Handle(TestMessage msg, CancellationToken ct) { }
            }
            """;

        var (_, _, trees) = RunGenerator(handlerSource, [new MediatorGenerator()], additionalReferences: [middlewareCompilation]);

        var wrapper = trees.FirstOrDefault(t => t.HintName.EndsWith("_Handler.g.cs"));

        // Internal middleware should NOT be included
        Assert.DoesNotContain("InternalMiddleware", wrapper.Source);

        // Public middleware should be included
        Assert.Contains("SharedMiddleware.PublicMiddleware.After", wrapper.Source);
    }

    [Fact]
    public void PrivateMiddlewareNotDiscoveredFromReferencedAssembly()
    {
        // Private middleware in referenced assembly should NOT be discovered
        var middlewareSource = """
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedMiddleware;

            public class Container
            {
                private class PrivateMiddleware
                {
                    public static void Before(object message) { }
                }
            }

            [Middleware]
            public static class PublicMiddleware
            {
                public static void After(object message) { }
            }
            """;

        var middlewareCompilation = CreateAssembly(middlewareSource, "MiddlewareAssembly");

        var handlerSource = """
            using System.Threading;
            using Foundatio.Mediator;

            public record TestMessage;

            public class TestHandler
            {
                public void Handle(TestMessage msg, CancellationToken ct) { }
            }
            """;

        var (_, _, trees) = RunGenerator(handlerSource, [new MediatorGenerator()], additionalReferences: [middlewareCompilation]);

        var wrapper = trees.FirstOrDefault(t => t.HintName.EndsWith("_Handler.g.cs"));

        // Private middleware should NOT be included
        Assert.DoesNotContain("PrivateMiddleware", wrapper.Source);

        // Public middleware should be included
        Assert.Contains("SharedMiddleware.PublicMiddleware.After", wrapper.Source);
    }

    [Fact]
    public void ExplicitOnlyMiddleware_NotAppliedGlobally_CrossAssembly()
    {
        // Regression: MetadataMiddlewareScanner was not reading ExplicitOnly from
        // the [Middleware] attribute, causing ExplicitOnly middleware (e.g. CachingMiddleware)
        // from referenced assemblies to be applied to ALL handlers instead of only those
        // that explicitly opt in via [UseMiddleware].
        var middlewareSource = """
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedMiddleware;

            [Middleware(Order = 100, ExplicitOnly = true)]
            public static class ExplicitCachingMiddleware
            {
                public static void Before(object message) { }
            }

            [Middleware(Order = 1)]
            public static class GlobalMiddleware
            {
                public static void Before(object message) { }
            }
            """;

        var middlewareCompilation = CreateAssembly(middlewareSource, "MiddlewareAssembly");

        var handlerSource = """
            using System.Threading;
            using Foundatio.Mediator;

            public record QueryMessage;

            public class QueryHandler
            {
                public string Handle(QueryMessage msg, CancellationToken ct) => "result";
            }
            """;

        var (_, _, trees) = RunGenerator(handlerSource, [new MediatorGenerator()], additionalReferences: [middlewareCompilation]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));

        // ExplicitOnly middleware from a referenced assembly should NOT be applied globally
        Assert.DoesNotContain("ExplicitCachingMiddleware", wrapper.Source);

        // Global middleware should still be applied
        Assert.Contains("SharedMiddleware.GlobalMiddleware.Before", wrapper.Source);
    }

    [Fact]
    public void ExplicitOnlyMiddleware_AppliedWhenReferenced_CrossAssembly()
    {
        // Verify ExplicitOnly middleware IS included when the handler opts in via [UseMiddleware]
        var middlewareSource = """
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedMiddleware;

            [Middleware(Order = 100, ExplicitOnly = true)]
            public static class ExplicitCachingMiddleware
            {
                public static void Before(object message) { }
            }
            """;

        var middlewareCompilation = CreateAssembly(middlewareSource, "MiddlewareAssembly");

        var handlerSource = """
            using System.Threading;
            using Foundatio.Mediator;
            using SharedMiddleware;

            public record CachedQuery;

            [UseMiddleware(typeof(ExplicitCachingMiddleware))]
            public class CachedQueryHandler
            {
                public string Handle(CachedQuery msg, CancellationToken ct) => "cached";
            }
            """;

        var (_, _, trees) = RunGenerator(handlerSource, [new MediatorGenerator()], additionalReferences: [middlewareCompilation]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));

        // ExplicitOnly middleware SHOULD be applied when explicitly referenced
        Assert.Contains("ExplicitCachingMiddleware", wrapper.Source);
    }

    [Fact]
    public void ExplicitOnlyMiddleware_MixedHandlers_CrossAssembly()
    {
        // Verify that when both an opted-in and an un-opted handler exist,
        // only the opted-in handler gets the ExplicitOnly middleware
        var middlewareSource = """
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedMiddleware;

            [Middleware(Order = 100, ExplicitOnly = true)]
            public static class ExplicitCachingMiddleware
            {
                public static void Before(object message) { }
            }
            """;

        var middlewareCompilation = CreateAssembly(middlewareSource, "MiddlewareAssembly");

        var handlerSource = """
            using System.Threading;
            using Foundatio.Mediator;
            using SharedMiddleware;

            public record GetOrders;
            public record GetCachedReport;

            public class OrderHandler
            {
                public string Handle(GetOrders msg, CancellationToken ct) => "orders";
            }

            [UseMiddleware(typeof(ExplicitCachingMiddleware))]
            public class ReportHandler
            {
                public string Handle(GetCachedReport msg, CancellationToken ct) => "report";
            }
            """;

        var (_, _, trees) = RunGenerator(handlerSource, [new MediatorGenerator()], additionalReferences: [middlewareCompilation]);

        var orderWrapper = trees.First(t => t.HintName.Contains("OrderHandler") && t.HintName.EndsWith("_Handler.g.cs"));
        var reportWrapper = trees.First(t => t.HintName.Contains("ReportHandler") && t.HintName.EndsWith("_Handler.g.cs"));

        // OrderHandler should NOT have ExplicitCachingMiddleware (no opt-in)
        Assert.DoesNotContain("ExplicitCachingMiddleware", orderWrapper.Source);

        // ReportHandler SHOULD have ExplicitCachingMiddleware (explicitly opted in)
        Assert.Contains("ExplicitCachingMiddleware", reportWrapper.Source);
    }

}
