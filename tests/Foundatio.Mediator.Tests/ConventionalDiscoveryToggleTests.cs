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

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

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

            [assembly: MediatorConfiguration(HandlerDiscovery = HandlerDiscovery.Explicit)]

            public record MyMessage;

            // Should NOT be discovered when conventional discovery is disabled
            public class MyMessageHandler
            {
                public void Handle(MyMessage m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

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

            [assembly: MediatorConfiguration(HandlerDiscovery = HandlerDiscovery.Explicit)]

            public record MyMessage;

            // Discovered via IHandler interface
            public class MessageProcessor : IHandler
            {
                public void Handle(MyMessage m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

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

            [assembly: MediatorConfiguration(HandlerDiscovery = HandlerDiscovery.Explicit)]

            public record MyMessage;

            // Discovered via [Handler] attribute on class
            [Handler]
            public class MessageProcessor
            {
                public void Handle(MyMessage m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

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

            [assembly: MediatorConfiguration(HandlerDiscovery = HandlerDiscovery.Explicit)]

            public record MyMessage;

            // Discovered via [Handler] attribute on method
            public class MessageProcessor
            {
                [Handler]
                public void Process(MyMessage m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

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

        // No configuration attribute - defaults to conventional discovery enabled
        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

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

            [assembly: MediatorConfiguration(HandlerDiscovery = HandlerDiscovery.Explicit)]

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

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        // Message1Handler should NOT be discovered (conventional only)
        Assert.DoesNotContain(trees, t => t.HintName.Contains("Message1_Handler.g.cs"));

        // Processor2 and Processor3 SHOULD be discovered (explicit)
        Assert.Contains(trees, t => t.HintName.Contains("Message2_Handler.g.cs"));
        Assert.Contains(trees, t => t.HintName.Contains("Message3_Handler.g.cs"));
    }

    // ── Consumer convention tests ──────────────────────────────────────────

    [Fact]
    public void ConsumerSuffix_IsDiscoveredByConvention()
    {
        var src = """
            using Foundatio.Mediator;

            public record OrderPlaced;

            public class OrderPlacedConsumer
            {
                public void Consume(OrderPlaced msg) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.Contains(trees, t => t.HintName.Contains("OrderPlaced_Handler.g.cs"));
    }

    [Fact]
    public void ConsumerSuffix_NotDiscoveredWhenExplicitOnly()
    {
        var src = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(HandlerDiscovery = HandlerDiscovery.Explicit)]

            public record OrderPlaced;

            public class OrderPlacedConsumer
            {
                public void Consume(OrderPlaced msg) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.DoesNotContain(trees, t => t.HintName.Contains("OrderPlaced_Handler.g.cs"));
    }

    [Theory]
    [InlineData("Consume")]
    [InlineData("ConsumeAsync")]
    [InlineData("Consumes")]
    [InlineData("ConsumesAsync")]
    public void ConsumerMethodNames_AreDiscovered(string methodName)
    {
        bool isAsync = methodName.EndsWith("Async");
        string returnType = isAsync ? "Task" : "void";
        string src = $$"""
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record EventMsg;

            public class EventMsgHandler
            {
                public {{returnType}} {{methodName}}(EventMsg msg)
                {{(isAsync ? "=> Task.CompletedTask;" : "{ }")}}
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.Contains(trees, t => t.HintName.Contains("EventMsg_Handler.g.cs"));
    }

    // ── FoundatioIgnore attribute tests ────────────────────────────────────

    [Fact]
    public void FoundatioIgnore_OnClass_PreventsDiscovery()
    {
        var src = """
            using Foundatio.Mediator;

            public record Msg;

            [FoundatioIgnore]
            public class MsgHandler
            {
                public void Handle(Msg m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.DoesNotContain(trees, t => t.HintName.Contains("Msg_Handler.g.cs"));
    }

    [Fact]
    public void FoundatioIgnore_OnMethod_PreventsDiscovery()
    {
        var src = """
            using Foundatio.Mediator;

            public record Msg;

            public class MsgHandler
            {
                [FoundatioIgnore]
                public void Handle(Msg m) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.DoesNotContain(trees, t => t.HintName.Contains("Msg_Handler.g.cs"));
    }

    [Fact]
    public void HandlerExcludeNamespacePatterns_ExcludesMatchingNamespace()
    {
        var src = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(HandlerExcludeNamespacePatterns = ["Ignored.Namespace.*"])]

            namespace Ignored.Namespace;

            public record IgnoredMessage;

            public class IgnoredMessageHandler
            {
                public void Handle(IgnoredMessage message) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.DoesNotContain(trees, t => t.HintName.Contains("IgnoredMessage_Handler.g.cs"));
    }

    [Fact]
    public void HandlerExcludeNamespacePatterns_ExcludesExplicitHandlersToo()
    {
        var src = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(HandlerExcludeNamespacePatterns = ["Ignored.Namespace"])]

            namespace Ignored.Namespace;

            public record ExplicitMessage;

            [Handler]
            public class ExplicitProcessor
            {
                public void Process(ExplicitMessage message) { }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);
        Assert.DoesNotContain(trees, t => t.HintName.Contains("ExplicitMessage_Handler.g.cs"));
    }

    [Fact]
    public void HandlerExcludeNamespacePatterns_DoesNotExcludeOtherNamespaces()
    {
        var src = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(HandlerExcludeNamespacePatterns = ["Ignored.Namespace.*"])]

            namespace Ignored.Namespace
            {
                public record IgnoredMessage;

                public class IgnoredMessageHandler
                {
                    public void Handle(IgnoredMessage message) { }
                }
            }

            namespace Allowed.Namespace
            {
                public record AllowedMessage;

                public class AllowedMessageHandler
                {
                    public void Handle(AllowedMessage message) { }
                }
            }
            """;

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        Assert.DoesNotContain(trees, t => t.HintName.Contains("IgnoredMessage_Handler.g.cs"));
        Assert.Contains(trees, t => t.HintName.Contains("AllowedMessage_Handler.g.cs"));
    }
}
