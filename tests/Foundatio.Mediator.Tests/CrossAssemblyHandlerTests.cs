using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Tests;

public class CrossAssemblyHandlerTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void DiscoversHandlerFromReferencedAssembly()
    {
        // Handler in a "referenced assembly" marked with [FoundatioModule]
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record SharedMessage;

            public class SharedHandler
            {
                public Task<string> HandleAsync(SharedMessage msg, CancellationToken ct)
                {
                    return Task.FromResult("handled");
                }
            }
            """;

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

        // Consumer assembly that calls the handler
        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task<string> CallHandler(CancellationToken ct)
                {
                    return await _mediator.InvokeAsync<string>(new SharedMessage(), ct);
                }
            }
            """;

        // assertCleanCompilation: false — cross-assembly interceptors reference handler wrapper types
        // that only exist in the referenced assembly's generator output (not available in test).
        var (_, diagnostics, trees) = RunGenerator(consumerSource, [new MediatorGenerator()], additionalReferences: [handlerAssembly], assertCleanCompilation: false);

        // Should not have any errors
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should generate cross-assembly interceptors
        var interceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.NotNull(interceptors.HintName);

        // The interceptor should call the handler wrapper from the referenced assembly
        Assert.Contains("SharedHandler_SharedMessage_Handler", interceptors.Source);
        Assert.Contains("InterceptSharedMessage_InvokeAsync", interceptors.Source);
    }

    [Fact]
    public void DiscoversHandlerWithHandlerAttribute()
    {
        // Handler with [Handler] attribute in referenced assembly
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record CustomMessage;

            [Handler]
            public class CustomProcessor
            {
                public Task<int> HandleAsync(CustomMessage msg, CancellationToken ct)
                {
                    return Task.FromResult(42);
                }
            }
            """;

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task<int> CallProcessor(CancellationToken ct)
                {
                    return await _mediator.InvokeAsync<int>(new CustomMessage(), ct);
                }
            }
            """;

        // assertCleanCompilation: false — cross-assembly interceptors reference handler wrapper types
        // that only exist in the referenced assembly's generator output (not available in test).
        var (_, diagnostics, trees) = RunGenerator(consumerSource, [new MediatorGenerator()], additionalReferences: [handlerAssembly], assertCleanCompilation: false);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var interceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.NotNull(interceptors.HintName);
        Assert.Contains("CustomProcessor_CustomMessage_Handler", interceptors.Source);
    }

    [Fact]
    public void DiscoversHandlerWithMethodHandlerAttribute()
    {
        // Handler method with [Handler] attribute in a non-handler-named class
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record WorkItem;

            public class WorkProcessor
            {
                [Handler]
                public Task<string> ProcessWorkItem(WorkItem item, CancellationToken ct)
                {
                    return Task.FromResult("done");
                }
            }
            """;

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task<string> DoWork(CancellationToken ct)
                {
                    return await _mediator.InvokeAsync<string>(new WorkItem(), ct);
                }
            }
            """;

        // assertCleanCompilation: false — cross-assembly interceptors reference handler wrapper types
        // that only exist in the referenced assembly's generator output (not available in test).
        var (_, diagnostics, trees) = RunGenerator(consumerSource, [new MediatorGenerator()], additionalReferences: [handlerAssembly], assertCleanCompilation: false);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var interceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.NotNull(interceptors.HintName);
        Assert.Contains("WorkProcessor_WorkItem_Handler", interceptors.Source);
    }

    [Fact]
    public void DiscoversHandlerEndingWithConsumer()
    {
        // Handler class ending with "Consumer" in referenced assembly
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record EventMessage;

            public class EventConsumer
            {
                public Task ConsumeAsync(EventMessage msg, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            public class App
            {
                private readonly IMediator _mediator;
                public App(IMediator mediator) => _mediator = mediator;

                public async Task SendEvent(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new EventMessage(), ct);
                }
            }
            """;

        // assertCleanCompilation: false — cross-assembly interceptors reference handler wrapper types
        // that only exist in the referenced assembly's generator output (not available in test).
        var (_, diagnostics, trees) = RunGenerator(consumerSource, [new MediatorGenerator()], additionalReferences: [handlerAssembly], assertCleanCompilation: false);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var interceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.NotNull(interceptors.HintName);
        Assert.Contains("EventConsumer_EventMessage_Handler", interceptors.Source);
    }

    [Fact]
    public void InternalHandlerNotDiscoveredFromReferencedAssembly()
    {
        // Internal handler in referenced assembly should NOT be discovered
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record InternalMessage;
            public record PublicMessage;

            internal class InternalHandler
            {
                public Task HandleAsync(InternalMessage msg, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
            }

            public class PublicHandler
            {
                public Task HandleAsync(PublicMessage msg, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task CallPublicHandler(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new PublicMessage(), ct);
                }
            }
            """;

        // assertCleanCompilation: false — cross-assembly interceptors reference handler wrapper types
        // that only exist in the referenced assembly's generator output (not available in test).
        var (_, _, trees) = RunGenerator(consumerSource, [new MediatorGenerator()], additionalReferences: [handlerAssembly], assertCleanCompilation: false);

        var interceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.NotNull(interceptors.HintName);

        // Public handler should be discovered
        Assert.Contains("PublicHandler_PublicMessage_Handler", interceptors.Source);

        // Internal handler should NOT be in the interceptors (we can't call it cross-assembly)
        Assert.DoesNotContain("InternalHandler", interceptors.Source);
    }

    [Fact]
    public void HandlerWithFoundatioIgnoreNotDiscovered()
    {
        // Handler with [FoundatioIgnore] attribute should not be discovered
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record IgnoredMessage;
            public record NormalMessage;

            [FoundatioIgnore]
            public class IgnoredHandler
            {
                public Task HandleAsync(IgnoredMessage msg, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
            }

            public class NormalHandler
            {
                public Task HandleAsync(NormalMessage msg, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task Call(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new NormalMessage(), ct);
                }
            }
            """;

        // assertCleanCompilation: false — cross-assembly interceptors reference handler wrapper types
        // that only exist in the referenced assembly's generator output (not available in test).
        var (_, _, trees) = RunGenerator(consumerSource, [new MediatorGenerator()], additionalReferences: [handlerAssembly], assertCleanCompilation: false);

        var interceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.NotNull(interceptors.HintName);

        // Normal handler should be discovered
        Assert.Contains("NormalHandler_NormalMessage_Handler", interceptors.Source);

        // Ignored handler should NOT be discovered
        Assert.DoesNotContain("IgnoredHandler", interceptors.Source);
    }

    [Fact]
    public void AssemblyWithoutFoundatioModuleNotScanned()
    {
        // Handler in assembly WITHOUT [FoundatioModule] should NOT be discovered
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            // Note: NO [assembly: FoundatioModule] attribute

            namespace OtherHandlers;

            public record OtherMessage;

            public class OtherHandler
            {
                public Task HandleAsync(OtherMessage msg, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using OtherHandlers;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task Call(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new OtherMessage(), ct);
                }
            }
            """;

        var (_, _, trees) = RunGenerator(consumerSource, [new MediatorGenerator()], additionalReferences: [handlerAssembly]);

        // Should NOT generate cross-assembly interceptors for handlers in non-module assemblies
        var interceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.Null(interceptors.HintName);
    }

    [Fact]
    public void LocalHandlerTakesPrecedenceOverCrossAssembly()
    {
        // Handler in referenced assembly
        var externalHandlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record SharedMessage;

            public class SharedHandler
            {
                public Task<string> HandleAsync(SharedMessage msg, CancellationToken ct)
                {
                    return Task.FromResult("external");
                }
            }
            """;

        var externalAssembly = CreateAssembly(externalHandlerSource, "HandlerAssembly");

        // Local handler for the same message type
        var localSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            // Local handler for SharedMessage - should take precedence
            public class LocalHandler
            {
                public Task<string> HandleAsync(SharedMessage msg, CancellationToken ct)
                {
                    return Task.FromResult("local");
                }
            }

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task<string> Call(CancellationToken ct)
                {
                    return await _mediator.InvokeAsync<string>(new SharedMessage(), ct);
                }
            }
            """;

        var (_, _, trees) = RunGenerator(localSource, [new MediatorGenerator()], additionalReferences: [externalAssembly]);

        // Should generate local handler wrapper
        var localWrapper = trees.FirstOrDefault(t => t.HintName.Contains("LocalHandler_SharedMessage_Handler"));
        Assert.NotNull(localWrapper.HintName);

        // Should NOT generate cross-assembly interceptors since local handler exists
        var crossInterceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.Null(crossInterceptors.HintName);
    }

    [Fact]
    public void GeneratesInterceptorWithCorrectReturnType()
    {
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record QueryMessage;

            public class QueryHandler
            {
                public Task<Result<string>> HandleAsync(QueryMessage msg, CancellationToken ct)
                {
                    return Task.FromResult(Result<string>.Success("result"));
                }
            }
            """;

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task<Result<string>> Query(CancellationToken ct)
                {
                    return await _mediator.InvokeAsync<Result<string>>(new QueryMessage(), ct);
                }
            }
            """;

        // assertCleanCompilation: false — cross-assembly interceptors reference handler wrapper types
        // that only exist in the referenced assembly's generator output (not available in test).
        var (_, diagnostics, trees) = RunGenerator(consumerSource, [new MediatorGenerator()], additionalReferences: [handlerAssembly], assertCleanCompilation: false);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var interceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.NotNull(interceptors.HintName);

        // Should have correct return type in interceptor
        Assert.Contains("ValueTask<Foundatio.Mediator.Result<string>>", interceptors.Source);
    }

    [Fact]
    public void NoInterceptorsGeneratedWhenInterceptorsDisabled()
    {
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record TestMessage;

            public class TestHandler
            {
                public Task HandleAsync(TestMessage msg, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var handlerAssembly = CreateAssembly(handlerSource, "HandlerAssembly");

        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            [assembly: MediatorConfiguration(DisableInterceptors = true)]

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task Call(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new TestMessage(), ct);
                }
            }
            """;

        var (_, _, trees) = RunGenerator(consumerSource, [new MediatorGenerator()], additionalReferences: [handlerAssembly]);

        // Should NOT generate cross-assembly interceptors when disabled
        var interceptors = trees.FirstOrDefault(t => t.HintName == "_CrossAssemblyInterceptors.g.cs");
        Assert.Null(interceptors.HintName);
    }

}
