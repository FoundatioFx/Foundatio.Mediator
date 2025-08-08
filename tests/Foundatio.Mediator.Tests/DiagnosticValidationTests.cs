namespace Foundatio.Mediator.Tests;

public class DiagnosticValidationTests : GeneratorTestBase
{
	private static readonly MediatorGenerator Gen = new();

	[Fact]
    public void FMED006_NoHandlerForInvoke()
	{
		var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Other;
			public class OtherHandler { public Task HandleAsync(Other m, CancellationToken ct) => Task.CompletedTask; }

			public static class Calls {
				public static async Task Call(IMediator m) {
					await m.InvokeAsync(new Msg());
				}
			}
			""";

		var (_, genDiags, _) = RunGenerator(src, [ Gen ]);
		Assert.Contains(genDiags, d => d.Id == "FMED006");
	}

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

		var (_, genDiags, _) = RunGenerator(src, [ Gen ]);
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
		var (_, genDiags, _) = RunGenerator(src, [ Gen ]);
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
		var (_, genDiags, _) = RunGenerator(src, [ Gen ]);
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
		var (_, genDiags, _) = RunGenerator(src, [ Gen ]);
		Assert.Contains(genDiags, d => d.Id == "FMED010");
	}

	[Fact]
	public void FMED006_GenericMessageArgument_NoDiagnostic()
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

		var (_, genDiags, _) = RunGenerator(src, [ Gen ]);
		Assert.DoesNotContain(genDiags, d => d.Id == "FMED006");
	}
}

