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

}
