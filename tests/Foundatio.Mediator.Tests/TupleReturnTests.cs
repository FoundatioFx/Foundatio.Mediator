namespace Foundatio.Mediator.Tests;

public class TupleReturnTests : GeneratorTestBase
{
    [Fact]
    public void PublishesCascadingMessages_FromTupleReturn()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Evt;

			public class MsgHandler {
				public (int, Evt?) Handle(Msg m) => (1, new Evt());
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("PublishCascadingMessagesAsync", wrapper.Source);
        Assert.Contains("PublishAsync(", wrapper.Source);
    }
}

