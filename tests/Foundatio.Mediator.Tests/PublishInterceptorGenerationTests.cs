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

            [assembly: MediatorConfiguration(NotificationPublishStrategy = NotificationPublishStrategy.ForeachAwait)]

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

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        Assert.Contains("InterceptPublishAsync_MyEvent_", publishInterceptor.Source);
        Assert.Contains("InterceptsLocation", publishInterceptor.Source);
        // Uses runtime DI lookup with caching
        Assert.Contains("GetPublishHandlersForType", publishInterceptor.Source);
        Assert.Contains("_handlers_MyEvent_", publishInterceptor.Source);
        // Should have exception aggregation
        Assert.Contains("AggregateException", publishInterceptor.Source);
        // Should pass mediator to handlers
        Assert.Contains("handlers[i](mediator", publishInterceptor.Source);
    }

    [Fact]
    public void GeneratesPublishInterceptorForTaskWhenAll()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(NotificationPublishStrategy = NotificationPublishStrategy.TaskWhenAll)]

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

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        Assert.Contains("InterceptPublishAsync_UserRegistered_", publishInterceptor.Source);
        // Uses runtime DI lookup with caching
        Assert.Contains("GetPublishHandlersForType", publishInterceptor.Source);
        Assert.Contains("_handlers_UserRegistered_", publishInterceptor.Source);
        // TaskWhenAll should have task array and sync completion check
        Assert.Contains("var tasks = new", publishInterceptor.Source);
        Assert.Contains("IsCompletedSuccessfully", publishInterceptor.Source);
    }

    [Fact]
    public void GeneratesPublishInterceptorForFireAndForget()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(NotificationPublishStrategy = NotificationPublishStrategy.FireAndForget)]

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

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        Assert.Contains("InterceptPublishAsync_ItemDeleted_", publishInterceptor.Source);
        // Uses runtime DI lookup with caching
        Assert.Contains("GetPublishHandlersForType", publishInterceptor.Source);
        Assert.Contains("_handlers_ItemDeleted_", publishInterceptor.Source);
        // FireAndForget should use Task.Run
        Assert.Contains("Task.Run", publishInterceptor.Source);
        // And swallow exceptions
        Assert.Contains("Fire and forget", publishInterceptor.Source);
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

        // No NotificationPublishStrategy attribute set - should default to ForeachAwait
        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        // Should generate publish interceptor with default ForeachAwait strategy
        Assert.NotNull(publishInterceptor.Source);
        // Uses runtime DI lookup with caching
        Assert.Contains("GetPublishHandlersForType", publishInterceptor.Source);
        Assert.Contains("_handlers_TestEvent_", publishInterceptor.Source);
        Assert.Contains("AggregateException", publishInterceptor.Source);
    }

    [Fact]
    public void DoesNotGeneratePublishInterceptorWhenInterceptorsDisabled()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableInterceptors = true)]

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

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

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

            [assembly: MediatorConfiguration(NotificationPublishStrategy = NotificationPublishStrategy.ForeachAwait)]

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

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

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

            [assembly: MediatorConfiguration(NotificationPublishStrategy = NotificationPublishStrategy.ForeachAwait)]

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

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        // Uses runtime DI lookup with caching
        Assert.Contains("GetPublishHandlersForType", publishInterceptor.Source);
        Assert.Contains("_handlers_TestEvent_", publishInterceptor.Source);
    }

    [Fact]
    public void InvalidPublisherValueFallsBackToDefault()
    {
        // With enum-based configuration, invalid values are caught at compile time.
        // This test verifies that the default (no attribute) produces ForeachAwait behavior.
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

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        // Default configuration produces ForeachAwait behavior
        var publishInterceptor = trees.FirstOrDefault(t => t.HintName == "_PublishInterceptors.g.cs");
        Assert.NotNull(publishInterceptor.Source);
        // Uses runtime DI lookup with caching
        Assert.Contains("GetPublishHandlersForType", publishInterceptor.Source);
        Assert.Contains("_handlers_TestEvent_", publishInterceptor.Source);
        Assert.Contains("AggregateException", publishInterceptor.Source);
    }
}
