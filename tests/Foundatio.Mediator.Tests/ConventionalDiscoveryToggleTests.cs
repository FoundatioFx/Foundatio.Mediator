using Foundatio.Xunit;

namespace Foundatio.Mediator.Tests;

public class ConventionalDiscoveryToggleTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void ConventionalDiscovery_Enabled_DiscoversHandlerByNamingConvention()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            // Discovered by naming convention (ends with Handler)
            public class MyMessageHandler
            {
                public void Handle(MyMessage m) { }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableConventionalDiscovery", "false"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // Handler should be discovered and a wrapper generated
        Assert.Contains(trees, t => t.HintName.Contains("MyMessage_Handler.g.cs"));
    }

    [Fact]
    public void ConventionalDiscovery_Disabled_DoesNotDiscoverHandlerByNamingConvention()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            // Should NOT be discovered when conventional discovery is disabled
            public class MyMessageHandler
            {
                public void Handle(MyMessage m) { }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableConventionalDiscovery", "true"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // Handler should NOT be discovered
        Assert.DoesNotContain(trees, t => t.HintName.Contains("MyMessage_Handler.g.cs"));
    }

    [Fact]
    public void ConventionalDiscovery_Disabled_DiscoversHandlerWithIHandlerInterface()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            // Discovered via IHandler interface
            public class MessageProcessor : IHandler
            {
                public void Handle(MyMessage m) { }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableConventionalDiscovery", "true"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // Handler should be discovered via IHandler interface
        Assert.Contains(trees, t => t.HintName.Contains("MyMessage_Handler.g.cs"));
    }

    [Fact]
    public void ConventionalDiscovery_Disabled_DiscoversHandlerWithClassAttribute()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            // Discovered via [Handler] attribute on class
            [Handler]
            public class MessageProcessor
            {
                public void Handle(MyMessage m) { }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableConventionalDiscovery", "true"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // Handler should be discovered via [Handler] attribute
        Assert.Contains(trees, t => t.HintName.Contains("MyMessage_Handler.g.cs"));
    }

    [Fact]
    public void ConventionalDiscovery_Disabled_DiscoversHandlerWithMethodAttribute()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            // Discovered via [Handler] attribute on method
            public class MessageProcessor
            {
                [Handler]
                public void Process(MyMessage m) { }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableConventionalDiscovery", "true"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // Handler should be discovered via [Handler] attribute on method
        Assert.Contains(trees, t => t.HintName.Contains("MyMessage_Handler.g.cs"));
    }

    [Fact]
    public void ConventionalDiscovery_Disabled_DefaultsToEnabledWhenPropertyNotSet()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            // Discovered by naming convention when property not set (default behavior)
            public class MyMessageHandler
            {
                public void Handle(MyMessage m) { }
            }
            """;

        // No options set - defaults to conventional discovery enabled
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], optionsProvider: null);

        // Handler should be discovered
        Assert.Contains(trees, t => t.HintName.Contains("MyMessage_Handler.g.cs"));
    }

    [Fact]
    public void ConventionalDiscovery_Disabled_MixedHandlers_OnlyExplicitHandlersGenerated()
    {
        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Message1;
            public record Message2;
            public record Message3;

            // Should NOT be discovered (naming convention only)
            public class Message1Handler
            {
                public void Handle(Message1 m) { }
            }

            // SHOULD be discovered (IHandler interface)
            public class Processor2 : IHandler
            {
                public void Handle(Message2 m) { }
            }

            // SHOULD be discovered ([Handler] attribute)
            [Handler]
            public class Processor3
            {
                public void Handle(Message3 m) { }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableConventionalDiscovery", "true"));
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // Message1Handler should NOT be discovered (conventional only)
        Assert.DoesNotContain(trees, t => t.HintName.Contains("Message1_Handler.g.cs"));

        // Processor2 and Processor3 SHOULD be discovered (explicit)
        Assert.Contains(trees, t => t.HintName.Contains("Message2_Handler.g.cs"));
        Assert.Contains(trees, t => t.HintName.Contains("Message3_Handler.g.cs"));
    }
}
