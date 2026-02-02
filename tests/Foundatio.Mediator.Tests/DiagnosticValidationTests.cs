using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Foundatio.Mediator.Tests;

public class DiagnosticValidationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    private static readonly MediatorGenerator Gen = new();

    [Fact]
    public void FMED007_MultipleHandlersForInvoke()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			public class H1Handler { public Task HandleAsync(Msg m, CancellationToken ct) => Task.CompletedTask; }
			public class H2Handler { public Task HandleAsync(Msg m, CancellationToken ct) => Task.CompletedTask; }

			public static class Calls {
				public static void Call(IMediator m) { m.Invoke(new Msg()); }
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED007");
    }

    [Fact]
    public void FMED008_SyncInvokeOnAsyncHandler()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public class HHandler { public Task HandleAsync(Msg m, CancellationToken ct) => Task.CompletedTask; }

			public static class Calls {
				public static void Call(IMediator m) { m.Invoke(new Msg()); }
			}
			""";
        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED008");
    }

    [Fact]
    public void FMED009_SyncInvokeWithAsyncMiddleware()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public class HHandler { public void Handle(Msg m, CancellationToken ct) { } }

			public static class M1Middleware {
				public static async Task BeforeAsync(Msg m, CancellationToken ct) { await Task.Yield(); }
			}

			public static class Calls {
				public static void Call(IMediator m) { m.Invoke(new Msg()); }
			}
			""";
        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED009");
    }

    [Fact]
    public void FMED010_SyncInvokeOnTupleReturnHandler()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Evt;
			public class HHandler { public (int, Evt?) Handle(Msg m) => (42, new Evt()); }

			public static class Calls {
				public static void Call(IMediator m) { m.Invoke(new Msg()); }
			}
			""";
        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED010");
    }

    [Fact]
    public void GenericMessageArgument_NoDiagnostic()
    {
        var src = """
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public static class Calls {
				public static async Task Call<T>(IMediator m, T msg) {
					await m.InvokeAsync(msg);
				}
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.DoesNotContain(genDiags, d => d.Id == "FMED007");
    }

    [Fact]
    public void FMED006_PrivateMiddlewareNotAllowed()
    {
        var src = """
			using System.Threading;
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			public class Container
			{
				private class PrivateMiddleware
				{
					public static void Before(Msg m) { }
				}
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED006" && d.GetMessage().Contains("PrivateMiddleware"));
    }

    [Fact]
    public void MiddlewareWithIgnoreAttribute_NoDiagnostic()
    {
        var src = """
			using System.Threading;
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			[FoundatioIgnore]
			public class IgnoredMiddleware
			{
				public static void Before(Msg m) { }
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.DoesNotContain(genDiags, d => d.Id == "FMED006");
    }

    [Fact]
    public void InternalMiddleware_NoError()
    {
        var src = """
			using System.Threading;
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			internal static class InternalMiddleware
			{
				public static void Before(Msg m) { }
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.DoesNotContain(genDiags, d => d.Id == "FMED006");
    }

    [Fact]
    public void FMED007_MultipleHandlersAcrossAssemblies()
    {
        // Handler in referenced assembly
        var handlerSource = """
			using System.Threading;
			using Foundatio.Mediator;

			[assembly: FoundatioModule]

			namespace SharedHandlers;

			public record SharedMessage;

			public class SharedHandler
			{
				public void Handle(SharedMessage msg, CancellationToken ct) { }
			}
			""";

        var handlerAssembly = CreateHandlerAssembly(handlerSource);

        // Local handler for the same message type
        var consumerSource = """
			using System.Threading;
			using Foundatio.Mediator;
			using SharedHandlers;

			public class LocalHandler
			{
				public void Handle(SharedMessage msg, CancellationToken ct) { }
			}

			public class Consumer
			{
				private readonly IMediator _mediator;
				public Consumer(IMediator mediator) => _mediator = mediator;

				public void Call()
				{
					_mediator.Invoke(new SharedMessage());
				}
			}
			""";

        var (_, diagnostics, _) = RunGenerator(consumerSource, [Gen], additionalReferences: [handlerAssembly]);

        Assert.Contains(diagnostics, d => d.Id == "FMED007" && d.GetMessage().Contains("referenced assembly"));
    }

    [Fact]
    public void FMED008_SyncInvokeOnAsyncCrossAssemblyHandler()
    {
        // Async handler in referenced assembly
        var handlerSource = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			[assembly: FoundatioModule]

			namespace SharedHandlers;

			public record SharedMessage;

			public class SharedHandler
			{
				public Task HandleAsync(SharedMessage msg, CancellationToken ct) => Task.CompletedTask;
			}
			""";

        var handlerAssembly = CreateHandlerAssembly(handlerSource);

        // Consumer using sync Invoke on async handler
        var consumerSource = """
			using Foundatio.Mediator;
			using SharedHandlers;

			public class Consumer
			{
				private readonly IMediator _mediator;
				public Consumer(IMediator mediator) => _mediator = mediator;

				public void Call()
				{
					_mediator.Invoke(new SharedMessage());
				}
			}
			""";

        var (_, diagnostics, _) = RunGenerator(consumerSource, [Gen], additionalReferences: [handlerAssembly]);

        Assert.Contains(diagnostics, d => d.Id == "FMED008" && d.GetMessage().Contains("referenced assembly"));
    }

    private static MetadataReference CreateHandlerAssembly(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IMediator).Assembly.Location),
        };

        var coreLibDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimePath = Path.Combine(coreLibDir, "System.Runtime.dll");
        var netstandardPath = Path.Combine(coreLibDir, "netstandard.dll");

        if (File.Exists(runtimePath))
            references.Add(MetadataReference.CreateFromFile(runtimePath));
        if (File.Exists(netstandardPath))
            references.Add(MetadataReference.CreateFromFile(netstandardPath));

        var compilation = CSharpCompilation.Create(
            assemblyName: "HandlerAssembly",
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
            throw new InvalidOperationException($"Failed to compile handler assembly:\n{errors}");
        }

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        return MetadataReference.CreateFromStream(ms);
    }
}
