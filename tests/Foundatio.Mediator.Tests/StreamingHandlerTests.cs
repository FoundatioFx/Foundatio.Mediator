using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Tests;

public class StreamingHandlerTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void GeneratesWrapperForAsyncEnumerableHandler()
    {
        var source = """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record StreamQuery(int Count);

            public class StreamHandler
            {
                public async IAsyncEnumerable<int> HandleAsync(
                    StreamQuery query,
                    [EnumeratorCancellation] CancellationToken cancellationToken = default)
                {
                    for (int i = 0; i < query.Count; i++)
                    {
                        yield return i;
                    }
                }
            }
            """;

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var handler = trees.FirstOrDefault(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.NotNull(handler.Source);
        Assert.Contains("StreamHandler", handler.Source);
        Assert.Contains("StreamQuery", handler.Source);
    }

    [Fact]
    public void GeneratesWrapperForStaticStreamingHandler()
    {
        var source = """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record StringStreamQuery(string Prefix);

            public static class StringStreamHandler
            {
                public static async IAsyncEnumerable<string> Handle(
                    StringStreamQuery query,
                    [EnumeratorCancellation] CancellationToken cancellationToken = default)
                {
                    yield return query.Prefix + "-1";
                    yield return query.Prefix + "-2";
                }
            }
            """;

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var handler = trees.FirstOrDefault(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.NotNull(handler.Source);
        Assert.Contains("StringStreamHandler", handler.Source);
    }

    [Fact]
    public void StreamingHandler_NoCompilationErrors()
    {
        var source = """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record NumberStreamQuery;

            public class NumberStreamHandler
            {
                public async IAsyncEnumerable<int> HandleAsync(
                    NumberStreamQuery query,
                    [EnumeratorCancellation] CancellationToken cancellationToken = default)
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        await Task.Delay(1, cancellationToken);
                        yield return i;
                    }
                }
            }
            """;

        var (compilation, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        // No errors from the generator
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Verify the handler wrapper was generated
        var handler = trees.FirstOrDefault(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.NotNull(handler.Source);
        Assert.Contains("NumberStreamHandler", handler.Source);
    }
}
