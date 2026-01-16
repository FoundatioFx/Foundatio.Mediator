using Foundatio.Xunit;

namespace Foundatio.Mediator.Tests;

public class OpenTelemetryTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void OpenTelemetry_Disabled_NoActivityCodeGenerated()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public static class Calls { public static void C(IMediator m) { m.Invoke(new Msg()); } }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "true"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.DoesNotContain("MediatorActivitySource", wrapper.Source);
        Assert.DoesNotContain("StartActivity", wrapper.Source);
        Assert.DoesNotContain("activity?.SetTag", wrapper.Source);
    }

    [Fact]
    public void OpenTelemetry_Enabled_ActivityCodeGenerated()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public static class Calls { public static void C(IMediator m) { m.Invoke(new Msg()); } }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "false"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("using var activity = MediatorActivitySource.Instance.StartActivity(", wrapper.Source);
        Assert.Contains("activity?.SetTag(\"messaging.message.type\", \"Msg\");", wrapper.Source);
    }

    [Fact]
    public void OpenTelemetry_DefaultValue_Enabled()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public static class Calls { public static void C(IMediator m) { m.Invoke(new Msg()); } }
            """;

        // Not setting MediatorDisableOpenTelemetry property - should default to enabled (false)
        var opts = CreateOptions();
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("MediatorActivitySource", wrapper.Source);
        Assert.Contains("StartActivity", wrapper.Source);
    }

    [Fact]
    public void OpenTelemetry_WithAsyncHandler_ActivityCodeGenerated()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record AsyncMsg;
            public class AsyncMsgHandler { public async Task<string> HandleAsync(AsyncMsg m, CancellationToken ct) { return "result"; } }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "false"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("using var activity = MediatorActivitySource.Instance.StartActivity(", wrapper.Source);
        Assert.Contains("activity?.SetTag(\"messaging.message.type\", \"AsyncMsg\");", wrapper.Source);
    }

    [Fact]
    public void OpenTelemetry_Enabled_SuccessStatusSet()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "false"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);", wrapper.Source);
    }

    [Fact]
    public void OpenTelemetry_WithMiddleware_IncludesExceptionHandling()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public class TestMiddleware { public void Finally(Msg m) { } }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "false"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);", wrapper.Source);
        Assert.Contains("activity?.SetTag(\"exception.type\", ex.GetType().FullName);", wrapper.Source);
        Assert.Contains("activity?.SetTag(\"exception.message\", ex.Message);", wrapper.Source);
    }

    [Fact]
    public void OpenTelemetry_ActivityAvailableToMiddleware_BeforeMethod()
    {
        var src = """
            using System.Diagnostics;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public class TracingMiddleware
            {
                public void Before(Msg m, Activity? activity)
                {
                    activity?.SetTag("custom.tag", "value");
                }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "false"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // Verify activity is passed to Before method (instance middleware uses camelCase variable name)
        Assert.Contains("tracingMiddleware.Before(message, activity)", wrapper.Source);
    }

    [Fact]
    public void OpenTelemetry_ActivityAvailableToMiddleware_AfterMethod()
    {
        var src = """
            using System.Diagnostics;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public class TracingMiddleware
            {
                public void After(Msg m, Activity? activity)
                {
                    activity?.SetTag("result.status", "success");
                }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "false"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // Verify activity is passed to After method (instance middleware uses camelCase variable name)
        Assert.Contains("tracingMiddleware.After(message, activity)", wrapper.Source);
    }

    [Fact]
    public void OpenTelemetry_ActivityAvailableToMiddleware_FinallyMethod()
    {
        var src = """
            using System.Diagnostics;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public class TracingMiddleware
            {
                public void Finally(Msg m, System.Diagnostics.Activity? activity, System.Exception? ex)
                {
                    if (ex != null)
                        activity?.SetTag("error", "true");
                }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "false"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // Verify activity is passed to Finally method (instance middleware uses camelCase variable name)
        Assert.Contains("tracingMiddleware.Finally(message, activity, exception)", wrapper.Source);
    }

    [Fact]
    public void OpenTelemetry_Disabled_ActivityNotAvailableToMiddleware()
    {
        var src = """
            using System.Diagnostics;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler { public void Handle(Msg m) { } }
            public class TracingMiddleware
            {
                public void Before(Msg m, Activity? activity) { }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableOpenTelemetry", "true"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // When OpenTelemetry is disabled, activity won't be available, so it should try DI
        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // Activity should be resolved via DI when OTel is disabled (using full type name in generated code)
        Assert.Contains("GetRequiredService<System.Diagnostics.Activity?>()", wrapper.Source);
    }
}
