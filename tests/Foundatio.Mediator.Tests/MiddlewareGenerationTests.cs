namespace Foundatio.Mediator.Tests;

public class MiddlewareGenerationTests : GeneratorTestBase
{
	[Fact]
	public void GeneratesBeforeAfterFinally_WithTryFinally()
	{
		var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			public static class MW1Middleware {
				public static void Before(Msg m) { }
				public static void After(Msg m) { }
				public static void Finally(Msg m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
		Assert.Contains("MW1Middleware.Before", wrapper.Source);
		Assert.Contains("MW1Middleware.After", wrapper.Source);
		Assert.Contains("MW1Middleware.Finally", wrapper.Source);
		Assert.Contains("try", wrapper.Source);
		Assert.Contains("finally", wrapper.Source);
	}

	[Fact]
	public void PassesHandlerResultToAfterAndFinally()
	{
		var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Response;

			public class MsgHandler { public Result<Response> Handle(Msg m, CancellationToken ct) => Result.Ok(new Response()); }

			public static class MW1Middleware {
				public static void After(Msg m, Result<Response> result) { }
				public static void Finally(Msg m, Result<Response> result) { }
			}
			""";

		var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);

		var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
		Assert.Contains("MW1Middleware.After(message, handlerResult)", wrapper.Source);
		Assert.Contains("MW1Middleware.Finally(message, handlerResult)", wrapper.Source);
	}

	[Fact]
	public void PassesHandlerTupleItemResultToAfterAndFinally()
	{
		var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Response;
			public record Event;

			public class MsgHandler { public (Result<Response> Response, Event? Event) Handle(Msg m, CancellationToken ct) => (Result.Ok(new Response()), null); }

			public static class MW1Middleware {
				public static void After(Msg m, Result<Response> result) { }
				public static void Finally(Msg m, Result<Response> result) { }
			}
			""";

		var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);

		var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
		Assert.Contains("MW1Middleware.After(message, handlerResult.Response)", wrapper.Source);
		Assert.Contains("MW1Middleware.Finally(message, handlerResult.Response)", wrapper.Source);
	}

	[Fact]
	public void PassesResultOfTToBaseResultParameter()
	{
		var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Response;

			public class MsgHandler { public Result<Response> Handle(Msg m, CancellationToken ct) => Result.Ok(new Response()); }

			public static class MW1Middleware {
				public static void After(Msg m, Result result) { }
				public static void Finally(Msg m, Result result) { }
			}
			""";

		var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);

		var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
		Assert.Contains("MW1Middleware.After(message, handlerResult!)", wrapper.Source);
		Assert.Contains("MW1Middleware.Finally(message, handlerResult!)", wrapper.Source);
	}

	[Fact]
	public void PassesResultOfTFromTupleToBaseResultParameter()
	{
		var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Response;
			public record Event;

			public class MsgHandler { public (Result<Response> Response, Event? Event) Handle(Msg m, CancellationToken ct) => (Result.Ok(new Response()), null); }

			public static class MW1Middleware {
				public static void After(Msg m, Result result) { }
				public static void Finally(Msg m, Result result) { }
			}
			""";

		var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);

		var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
		Assert.Contains("MW1Middleware.After(message, handlerResult.Response!)", wrapper.Source);
		Assert.Contains("MW1Middleware.Finally(message, handlerResult.Response!)", wrapper.Source);
	}

	[Fact]
	public void GeneratesAssemblyAttributeForMiddlewareOnly()
	{
		var src = """
			using Foundatio.Mediator;

			public static class LoggingMiddleware
			{
				public static void Before(object message) { }
			}
			""";

		var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);

		// Should generate assembly attribute even when there are no handlers
		var assemblyAttr = trees.Where(t => t.HintName.Contains("_FoundatioModuleAttribute.g.cs")).ToList();
		Assert.Single(assemblyAttr);
		Assert.Contains("[assembly: Foundatio.Mediator.FoundatioModule]", assemblyAttr[0].Source);
	}

	[Fact]
	public void GeneratesAssemblyAttributeForHandlers()
	{
		var src = """
			using System.Threading;
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }
			""";

		var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);

		// Should generate assembly attribute when there are handlers
		var assemblyAttr = trees.Where(t => t.HintName.Contains("_FoundatioModuleAttribute.g.cs")).ToList();
		Assert.Single(assemblyAttr);
		Assert.Contains("[assembly: Foundatio.Mediator.FoundatioModule]", assemblyAttr[0].Source);
	}

	[Fact]
	public void DoesNotGenerateAssemblyAttributeWhenNoHandlersOrMiddleware()
	{
		var src = """
			using Foundatio.Mediator;

			public record Msg;
			""";

		var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ]);

		// Should NOT generate assembly attribute when there are no handlers or middleware
		var assemblyAttr = trees.Where(t => t.HintName.Contains("_FoundatioModuleAttribute.g.cs")).ToList();
		Assert.Empty(assemblyAttr);
	}
}


