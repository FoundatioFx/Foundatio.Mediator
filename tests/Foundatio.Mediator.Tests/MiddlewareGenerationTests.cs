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
}

