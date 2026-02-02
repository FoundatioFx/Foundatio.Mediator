namespace Foundatio.Mediator.Tests;

public class PublishInterceptorGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void GeneratesPublishInterceptorForForeachAwait()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyEvent;

            public class MyEventHandler {
                public Task HandleAsync(MyEvent evt, CancellationToken ct) => Task.CompletedTask;
            }

            public class Publisher {
                public async Task Publish(IMediator mediator) {
                    await mediator.PublishAsync(new MyEvent());
                }
            }
            """;

        var options = CreateOptions(
            ("build_property.MediatorNotificationPublisher", "ForeachAwait"));

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()], options);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        Assert.Contains("InterceptPublishAsync_MyEvent_", publishInterceptor.Source);
        Assert.Contains("InterceptsLocation", publishInterceptor.Source);
        Assert.Contains("MyEventHandler_MyEvent_Handler", publishInterceptor.Source);
        // Should have exception aggregation
        Assert.Contains("AggregateException", publishInterceptor.Source);
        // Should pass mediator to handlers (each handler creates its own scope internally)
        Assert.Contains("HandleAsync(mediator", publishInterceptor.Source);
    }

    [Fact]
    public void GeneratesPublishInterceptorForTaskWhenAll()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record UserRegistered(string UserId);

            public class UserRegisteredHandler {
                public Task HandleAsync(UserRegistered evt, CancellationToken ct) => Task.CompletedTask;
            }

            public class Publisher {
                public async Task Publish(IMediator mediator) {
                    await mediator.PublishAsync(new UserRegistered("user123"));
                }
            }
            """;

        var options = CreateOptions(
            ("build_property.MediatorNotificationPublisher", "TaskWhenAll"));

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()], options);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        Assert.Contains("InterceptPublishAsync_UserRegistered_", publishInterceptor.Source);
        Assert.Contains("UserRegisteredHandler_UserRegistered_Handler", publishInterceptor.Source);
        // TaskWhenAll should have task variable and sync completion check
        Assert.Contains("t0", publishInterceptor.Source);
        Assert.Contains("IsCompletedSuccessfully", publishInterceptor.Source);
    }

    [Fact]
    public void GeneratesPublishInterceptorForFireAndForget()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record ItemDeleted(string ItemId);

            public class ItemDeletedHandler {
                public Task HandleAsync(ItemDeleted evt, CancellationToken ct) => Task.CompletedTask;
            }

            public class Publisher {
                public async Task Publish(IMediator mediator) {
                    await mediator.PublishAsync(new ItemDeleted("item1"));
                }
            }
            """;

        var options = CreateOptions(
            ("build_property.MediatorNotificationPublisher", "FireAndForget"));

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()], options);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        Assert.Contains("InterceptPublishAsync_ItemDeleted_", publishInterceptor.Source);
        // FireAndForget should use Task.Run
        Assert.Contains("Task.Run", publishInterceptor.Source);
        // And swallow exceptions
        Assert.Contains("fire and forget", publishInterceptor.Source);
    }

    [Fact]
    public void GeneratesPublishInterceptorWithDefaultForeachAwait()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record TestEvent;

            public class TestHandler {
                public void Handle(TestEvent evt) { }
            }

            public class Publisher {
                public async Task Publish(IMediator mediator) {
                    await mediator.PublishAsync(new TestEvent());
                }
            }
            """;

        // No MediatorNotificationPublisher property set - should default to ForeachAwait
        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        // Should generate publish interceptor with default ForeachAwait strategy
        Assert.NotNull(publishInterceptor.Source);
        Assert.Contains("TestHandler_TestEvent_Handler", publishInterceptor.Source);
        Assert.Contains("AggregateException", publishInterceptor.Source);
    }

    [Fact]
    public void DoesNotGeneratePublishInterceptorWhenInterceptorsDisabled()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record TestEvent;

            public class TestHandler {
                public void Handle(TestEvent evt) { }
            }

            public class Publisher {
                public async Task Publish(IMediator mediator) {
                    await mediator.PublishAsync(new TestEvent());
                }
            }
            """;

        // MediatorDisableInterceptors set to true - disables all interceptors including publish
        var options = CreateOptions(
            ("build_property.MediatorDisableInterceptors", "true"));

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()], options);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        // Should not generate publish interceptor when interceptors are disabled
        Assert.Null(publishInterceptor.Source);
    }

    [Fact]
    public void HandlesCascadingMessagesInPublishHandler()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record OrderPlaced(string OrderId);
            public record InventoryReserved(string OrderId);

            public class OrderPlacedHandler {
                public Task<(bool Success, InventoryReserved? Event)> HandleAsync(OrderPlaced evt, CancellationToken ct)
                    => Task.FromResult((true, (InventoryReserved?)new InventoryReserved(evt.OrderId)));
            }

            public class InventoryReservedHandler {
                public Task HandleAsync(InventoryReserved evt, CancellationToken ct) => Task.CompletedTask;
            }

            public class Publisher {
                public async Task Publish(IMediator mediator) {
                    await mediator.PublishAsync(new OrderPlaced("123"));
                }
            }
            """;

        var options = CreateOptions(
            ("build_property.MediatorNotificationPublisher", "ForeachAwait"));

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()], options);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        // Should handle cascading messages
        Assert.Contains("PublishAsync", publishInterceptor.Source);
    }

    [Fact]
    public void GeneratesInterceptorForSyncHandler()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record TestEvent;

            public class TestEventHandler {
                public void Handle(TestEvent evt) { }
            }

            public class Publisher {
                public async Task Publish(IMediator mediator) {
                    await mediator.PublishAsync(new TestEvent());
                }
            }
            """;

        var options = CreateOptions(
            ("build_property.MediatorNotificationPublisher", "ForeachAwait"));

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()], options);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        Assert.Contains("TestEventHandler_TestEvent_Handler", publishInterceptor.Source);
    }

    [Fact]
    public void InvalidPublisherValueFallsBackToDefault()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record TestEvent;

            public class TestHandler {
                public void Handle(TestEvent evt) { }
            }

            public class Publisher {
                public async Task Publish(IMediator mediator) {
                    await mediator.PublishAsync(new TestEvent());
                }
            }
            """;

        var options = CreateOptions(
            ("build_property.MediatorNotificationPublisher", "InvalidValue"));

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()], options);

        // Invalid value falls back to default ForeachAwait
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        Assert.Contains("TestHandler_TestEvent_Handler", publishInterceptor.Source);
        Assert.Contains("AggregateException", publishInterceptor.Source);
    }
}
