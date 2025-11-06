using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Foundatio.Mediator.Tests;

public class CrossAssemblyMiddlewareTests : GeneratorTestBase
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

        var middlewareCompilation = CreateMiddlewareAssembly(middlewareSource);

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

        var externalMiddlewareCompilation = CreateMiddlewareAssembly(externalMiddlewareSource);

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

        var middlewareCompilation = CreateMiddlewareAssembly(middlewareSource);

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

    private static MetadataReference CreateMiddlewareAssembly(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location), // System.Private.CoreLib
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location), // System.Linq
            MetadataReference.CreateFromFile(typeof(IMediator).Assembly.Location), // Foundatio.Mediator.Abstractions
        };

        // Add reference to System.Runtime and netstandard for base types (Attribute, ValueType, etc.)
        var coreLibDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimePath = Path.Combine(coreLibDir, "System.Runtime.dll");
        var netstandardPath = Path.Combine(coreLibDir, "netstandard.dll");

        if (File.Exists(runtimePath))
            references.Add(MetadataReference.CreateFromFile(runtimePath));
        if (File.Exists(netstandardPath))
            references.Add(MetadataReference.CreateFromFile(netstandardPath));

        var compilation = CSharpCompilation.Create(
            assemblyName: "MiddlewareAssembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new System.IO.MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"Failed to compile middleware assembly:\n{errors}");
        }

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        return MetadataReference.CreateFromStream(ms);
    }
}
