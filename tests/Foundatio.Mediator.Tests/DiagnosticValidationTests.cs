using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Tests;

public class DiagnosticValidationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    private static readonly MediatorGenerator Gen = new();

    // ── Middleware diagnostics ────────────────────────────────────────────

    [Fact]
    public void FMED001_MultipleBeforeMethodsInMiddleware()
    {
        var src = """
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m) { } }

			public class BadMiddleware
			{
				public void Before(Msg m) { }
				public void BeforeAsync(Msg m) { }
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED001" && d.GetMessage().Contains("BadMiddleware"));
    }

    [Fact]
    public void FMED002_MultipleAfterMethodsInMiddleware()
    {
        var src = """
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m) { } }

			public class BadMiddleware
			{
				public void Before(Msg m) { }
				public void After(Msg m) { }
				public void AfterAsync(Msg m) { }
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED002" && d.GetMessage().Contains("BadMiddleware"));
    }

    [Fact]
    public void FMED003_MultipleFinallyMethodsInMiddleware()
    {
        var src = """
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m) { } }

			public class BadMiddleware
			{
				public void Before(Msg m) { }
				public void Finally(Msg m) { }
				public void FinallyAsync(Msg m) { }
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED003" && d.GetMessage().Contains("BadMiddleware"));
    }

    [Fact]
    public void FMED004_MixedStaticAndInstanceMiddlewareMethods()
    {
        var src = """
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m) { } }

			public class BadMiddleware
			{
				public static void Before(Msg m) { }
				public void After(Msg m) { }
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen], assertCleanCompilation: false);
        Assert.Contains(genDiags, d => d.Id == "FMED004" && d.GetMessage().Contains("BadMiddleware"));
    }

    [Fact]
    public void FMED005_MiddlewareMessageTypeMismatch()
    {
        var src = """
			using Foundatio.Mediator;

			public record MsgA;
			public record MsgB;
			public class MsgAHandler { public void Handle(MsgA m) { } }

			public class BadMiddleware
			{
				public void Before(MsgA m) { }
				public void After(MsgB m) { }
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen], assertCleanCompilation: false);
        Assert.Contains(genDiags, d => d.Id == "FMED005" && d.GetMessage().Contains("BadMiddleware"));
    }

    [Fact]
    public void FMED011_MultipleExecuteMethodsInMiddleware()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m) { } }

			public class BadMiddleware
			{
				public Task ExecuteAsync(Msg m, HandlerExecutionDelegate next, CancellationToken ct)
					=> next();
				public Task ExecuteAsync(Msg m, HandlerExecutionDelegate next)
					=> next();
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen], assertCleanCompilation: false);
        Assert.Contains(genDiags, d => d.Id == "FMED011" && d.GetMessage().Contains("BadMiddleware"));
    }

    // ── Call-site diagnostics ──────────────────────────────────────────────

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
        var (_, genDiags, _) = RunGenerator(src, [Gen], assertCleanCompilation: false);
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

        var (_, genDiags, _) = RunGenerator(src, [Gen], assertCleanCompilation: false);
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

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

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

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

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

        // assertCleanCompilation: false — cross-assembly interceptors reference handler wrapper types
        // that only exist in the referenced assembly's generator output (not available in test).
        var (_, diagnostics, _) = RunGenerator(consumerSource, [Gen], additionalReferences: [handlerAssembly], assertCleanCompilation: false);

        Assert.Contains(diagnostics, d => d.Id == "FMED008" && d.GetMessage().Contains("referenced assembly"));
    }

    [Fact]
    public void FMED013_UseMiddleware_WithUnknownType_EmitsWarning()
    {
        // MissingMw is a real type that compiles but is NOT discovered as middleware
        // because it doesn't follow naming conventions and has no [Middleware] attribute.
        var src = """
			using Foundatio.Mediator;

			public record Msg;

			[UseMiddleware(typeof(MissingMw))]
			public class MsgHandler { public void Handle(Msg m) { } }

			public class MissingMw
			{
				public static void Before(object m) { }
			}
			""";

        var (_, genDiags, _) = RunGenerator(src, [Gen]);
        Assert.Contains(genDiags, d => d.Id == "FMED013" && d.GetMessage().Contains("MissingMw"));
    }
}
