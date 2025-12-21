namespace Foundatio.Mediator.Tests;

public class InterceptorsToggleTests : GeneratorTestBase
{
    [Fact]
    public void Interceptors_Disabled_NoAttributeOrInterceptsMethods()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public static class Calls { public static void C(IMediator m) { m.Invoke(new Msg()); } }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableInterceptors", "true"));
        var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ], opts);

        Assert.DoesNotContain(trees, t => t.HintName == "InterceptsLocationAttribute.g.cs");
        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.DoesNotContain("InterceptsLocation(", wrapper.Source);
    }

    [Fact]
    public void Interceptors_Enabled_HasAttributeAndIntercepts()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public static class Calls { public static void C(IMediator m) { m.Invoke(new Msg()); } }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableInterceptors", "false"));
        var (_, _, trees) = RunGenerator(src, [ new MediatorGenerator() ], opts);

        Assert.Contains(trees, t => t.HintName == "_InterceptsLocationAttribute.g.cs");
        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("InterceptsLocation(", wrapper.Source);
    }
}
