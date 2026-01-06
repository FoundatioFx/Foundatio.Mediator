using Foundatio.Xunit;

namespace Foundatio.Mediator.Tests;

public class SyncHandlerAsyncCallTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void SyncHandler_WithReturnValue_CalledViaInvokeAsync_GeneratesValidCode()
    {
        // This tests the bug where calling InvokeAsync<TResponse> on a sync handler
        // that returns a value should generate valid code that wraps the result in ValueTask

        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record GetGreeting(string Name);
            public class GreetingHandler
            {
                public string Handle(GetGreeting msg) => $"Hello, {msg.Name}!";
            }
            public static class Caller
            {
                public static async Task Test(IMediator m)
                {
                    // Call sync handler via InvokeAsync<TResponse>
                    var result = await m.InvokeAsync<string>(new GetGreeting("World"));
                }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableInterceptors", "false"));
        var (compilation, diagnostics, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // Should not have any errors
        var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        // Find the handler wrapper
        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));

        // Should have an interceptor for InvokeAsync
        Assert.Contains("InterceptInvokeAsync", wrapper.Source);

        // The interceptor should return ValueTask<string>, not just string
        Assert.Contains("System.Threading.Tasks.ValueTask<string>", wrapper.Source);

        // The interceptor should call HandleAsync (renamed to Handle for consistency with method name) which contains logging/telemetry
        // and wrap the result in ValueTask since HandleAsync returns string but interceptor must return ValueTask<string>
        // The test handler has no constructor params, so it qualifies for singleton fast path
        Assert.Contains("return new System.Threading.Tasks.ValueTask<string>(Handle((System.IServiceProvider)mediator, typedMessage, cancellationToken));", wrapper.Source);
    }

    [Fact]
    public void SyncHandler_Void_CalledViaInvokeAsync_GeneratesValidCode()
    {
        // Sync void handler called via InvokeAsync should work (returns ValueTask.CompletedTask)

        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message);
            public class PingHandler
            {
                public void Handle(Ping msg) { }
            }
            public static class Caller
            {
                public static async Task Test(IMediator m)
                {
                    await m.InvokeAsync(new Ping("Hello"));
                }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableInterceptors", "false"));
        var (compilation, diagnostics, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));

        // Should return default for void sync handlers (default(ValueTask) == ValueTask.CompletedTask)
        Assert.Contains("return default;", wrapper.Source);
    }

    [Fact]
    public void SyncHandler_WithTupleReturn_CalledViaInvokeAsync_GeneratesValidCode()
    {
        // Sync handler returning a tuple called via InvokeAsync<TResponse>
        // Should generate valid async code that wraps result in ValueTask

        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record CreateOrder(string CustomerId);
            public record Order(string Id, string CustomerId);
            public record OrderCreated(string OrderId);

            public class OrderHandler
            {
                public (Order Order, OrderCreated Event) Handle(CreateOrder cmd)
                {
                    var order = new Order("123", cmd.CustomerId);
                    return (order, new OrderCreated(order.Id));
                }
            }
            public static class Caller
            {
                public static async Task Test(IMediator m)
                {
                    // Call sync tuple handler via InvokeAsync<TResponse>
                    var result = await m.InvokeAsync<Order>(new CreateOrder("CUST-001"));
                }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableInterceptors", "false"));
        var (compilation, diagnostics, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // Should not have any generator errors
        var generatorErrors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.Empty(generatorErrors);

        // Find the handler wrapper
        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));

        // Should have an interceptor for InvokeAsync
        Assert.Contains("InterceptInvokeAsync", wrapper.Source);

        // The interceptor should return ValueTask<Order>
        Assert.Contains("System.Threading.Tasks.ValueTask<Order>", wrapper.Source);

        // The interceptor method should be async (to allow await on PublishAsync)
        Assert.Contains("public static async System.Threading.Tasks.ValueTask<Order> InterceptInvokeAsync", wrapper.Source);
    }

    [Fact]
    public void SyncHandler_WithTupleReturn_CalledViaSyncInvoke_GeneratesError()
    {
        // Sync handler returning a tuple cannot be called via sync Invoke
        // because the cascading messages need async PublishAsync
        // This should generate an error diagnostic

        var src = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record CreateOrder(string CustomerId);
            public record Order(string Id, string CustomerId);
            public record OrderCreated(string OrderId);

            public class OrderHandler
            {
                public (Order Order, OrderCreated Event) Handle(CreateOrder cmd)
                {
                    var order = new Order("123", cmd.CustomerId);
                    return (order, new OrderCreated(order.Id));
                }
            }
            public static class Caller
            {
                public static void Test(IMediator m)
                {
                    // Call sync tuple handler via sync Invoke - should error
                    var result = m.Invoke<Order>(new CreateOrder("CUST-001"));
                }
            }
            """;

        var opts = CreateOptions(("build_property.MediatorDisableInterceptors", "false"));
        var (compilation, diagnostics, trees) = RunGenerator(src, [new MediatorGenerator()], opts);

        // Should have an error about using sync Invoke with tuple-returning handler
        var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Id == "FMED010" || e.GetMessage().Contains("tuple") || e.GetMessage().Contains("InvokeAsync"));
    }
}
