using Foundatio.Xunit;

namespace Foundatio.Mediator.Tests;

/// <summary>
/// Tests for handlers nested inside generic classes.
/// </summary>
public class NestedInGenericClassTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void HandlerNestedInGenericClass_IsSkipped()
    {
        // Handlers nested in generic classes cannot be supported because they would produce
        // invalid code with unbound type parameters (e.g., OuterClass<T>.NestedHandler).
        // The generator should skip such handlers.
        const string source = """
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record TestMessage(string Value);

            public class OuterClass<T>
            {
                public class NestedHandler
                {
                    public Task HandleAsync(TestMessage msg)
                    {
                        return Task.CompletedTask;
                    }
                }
            }
            """;

        var (compilation, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        // Handler should be skipped - no handler wrapper generated
        var handlerFile = trees.FirstOrDefault(t => t.HintName.Contains("NestedHandler_TestMessage_Handler.g.cs"));
        Assert.Null(handlerFile.HintName);

        // DI registration should not contain references to the skipped handler
        var diFile = trees.FirstOrDefault(t => t.HintName.Contains("_MediatorHandlers.g.cs"));
        Assert.Null(diFile.HintName); // No handlers means no DI registration file

        // Should compile without errors
        var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void HandlerInNonGenericNestedClass_ShouldWork()
    {
        // Handler in a non-generic nested class should work fine
        const string source = """
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record TestMessage(string Value);

            public class OuterClass
            {
                public class NestedHandler
                {
                    public Task HandleAsync(TestMessage msg)
                    {
                        return Task.CompletedTask;
                    }
                }
            }
            """;

        var (compilation, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        // Should not have any errors
        var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        // Should have generated the handler
        var handlerFile = trees.FirstOrDefault(t => t.HintName.Contains("NestedHandler_TestMessage_Handler.g.cs"));
        Assert.NotNull(handlerFile.HintName);
    }

    [Fact]
    public void HandlerDeeplyNestedInGenericClass_IsSkipped()
    {
        // Handlers nested multiple levels inside generic classes should also be skipped
        const string source = """
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record TestMessage(string Value);

            public class OuterClass<T>
            {
                public class MiddleClass
                {
                    public class DeepHandler
                    {
                        public Task HandleAsync(TestMessage msg)
                        {
                            return Task.CompletedTask;
                        }
                    }
                }
            }
            """;

        var (compilation, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        // Handler should be skipped - no handler wrapper generated
        var handlerFile = trees.FirstOrDefault(t => t.HintName.Contains("DeepHandler_TestMessage_Handler.g.cs"));
        Assert.Null(handlerFile.HintName);

        // Should compile without errors
        var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void MiddlewareNestedInGenericClass_IsSkipped()
    {
        // Middleware nested in generic classes should also be skipped
        const string source = """
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record TestMessage(string Value);

            public class TestHandler
            {
                public Task HandleAsync(TestMessage msg) => Task.CompletedTask;
            }

            public class OuterClass<T>
            {
                public class LoggingMiddleware
                {
                    public void Before(object message) { }
                }
            }
            """;

        var (compilation, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        // Handler should still be generated
        var handlerFile = trees.FirstOrDefault(t => t.HintName.Contains("TestHandler_TestMessage_Handler.g.cs"));
        Assert.NotNull(handlerFile.HintName);

        // The middleware should be skipped - handler wrapper should not reference it
        Assert.DoesNotContain("LoggingMiddleware", handlerFile.Source);

        // Should compile without errors
        var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }
}
