namespace Foundatio.Mediator.Tests;

/// <summary>
/// PublishAsync interceptors resolve handlers for the call site's static type, but the runtime
/// publishes by the message's runtime type. These tests pin down when interception is allowed.
/// </summary>
public class PublishInterceptorTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    private const string Handlers = """
        using System.Threading.Tasks;
        using Foundatio.Mediator;

        public interface IOrderEvent { string OrderId { get; } }
        public record OrderCreated(string OrderId) : IOrderEvent;
        public sealed record OrderShipped(string OrderId) : IOrderEvent;
        public abstract record AuditEvent(string Source);
        public record UserAudited(string Source, string User) : AuditEvent(Source);

        public class OrderEventHandler
        {
            public Task HandleAsync(IOrderEvent evt) => Task.CompletedTask;
        }

        public class OrderCreatedHandler
        {
            public void Handle(OrderCreated evt) { }
        }

        public class UserAuditedHandler
        {
            public void Handle(UserAudited evt) { }
        }
        """;

    [Fact]
    public void ObjectInterfaceAndAbstractPublishSites_AreNotIntercepted()
    {
        var source = Handlers + """

            public class Publisher(IMediator mediator)
            {
                public Task ByObject(object notification) => mediator.PublishAsync(notification).AsTask();
                public Task ByInterface(IOrderEvent evt) => mediator.PublishAsync(evt).AsTask();
                public Task ByAbstract(AuditEvent evt) => mediator.PublishAsync(evt).AsTask();
                public Task Generic<T>(T evt) where T : class => mediator.PublishAsync(evt).AsTask();
                public Task ByText() => mediator.PublishAsync("plain text").AsTask();
            }
            """;

        var (_, _, trees) = RunGenerator(source, [new MediatorGenerator()]);

        var interceptors = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs").Source;
        Assert.NotNull(interceptors);

        // Only the string publish has a compile-time type the runtime will agree with
        Assert.Contains("typeof(global::System.String)", interceptors);
        Assert.DoesNotContain("IOrderEvent", interceptors);
        Assert.DoesNotContain("AuditEvent", interceptors);
        Assert.DoesNotContain("global::object", interceptors);
        Assert.Equal(1, CountOccurrences(interceptors, "[InterceptsLocation("));
    }

    [Fact]
    public void NonSealedMessage_FallsBackToRuntimeDispatchWhenDerived()
    {
        var source = Handlers + """

            public class Publisher(IMediator mediator)
            {
                public Task Created(OrderCreated evt) => mediator.PublishAsync(evt).AsTask();
                public Task Shipped(OrderShipped evt) => mediator.PublishAsync(evt).AsTask();
            }
            """;

        var (_, _, trees) = RunGenerator(source, [new MediatorGenerator()]);
        var interceptors = trees.First(t => t.HintName == "_PublishInterceptors.g.cs").Source;

        // OrderCreated is an unsealed record: a derived record could be published through this site
        Assert.Contains("if (message.GetType() != typeof(global::OrderCreated)) return mediator.PublishAsync(message, cancellationToken);", interceptors);

        // OrderShipped is sealed: no guard needed
        Assert.DoesNotContain("typeof(global::OrderShipped)) return mediator.PublishAsync", interceptors);
        Assert.Contains("registry.GetPublishHandlersForType(typeof(global::OrderShipped))", interceptors);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
