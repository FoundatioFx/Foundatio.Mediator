using Foundatio.Mediator.Tests.Fixtures;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_InvokeAsyncTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    [Fact]
    public async Task InvokeAsync_ReturnsExpected()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<PingHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.InvokeAsync<string>(new Ping("Ping"), TestCancellationToken);
        Assert.Equal("Ping Pong", result);
    }

    // ── Consumer method name convention ────────────────────────────────────

    public record ConsumeEvent(string Name);

    public class ConsumeEventConsumer
    {
        public static string? LastConsumed { get; set; }
        public void Consume(ConsumeEvent evt) => LastConsumed = evt.Name;
    }

    [Fact]
    public async Task InvokeAsync_WithConsumeMethod_IsDiscoveredAndExecuted()
    {
        ConsumeEventConsumer.LastConsumed = null;

        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<ConsumeEventConsumer>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.InvokeAsync(new ConsumeEvent("hello"), TestCancellationToken);
        Assert.Equal("hello", ConsumeEventConsumer.LastConsumed);
    }

    // ── No handler registered ──────────────────────────────────────────────

    public record UnhandledMessage(string Value);

    [Fact]
    public async Task InvokeAsync_WithNoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddMediator();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.InvokeAsync(new UnhandledMessage("test"), TestCancellationToken).AsTask());

        Assert.Contains("No handler found", ex.Message);
    }

    [Fact]
    public async Task InvokeAsync_WithResponse_NoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddMediator();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.InvokeAsync<string>(new UnhandledMessage("test"), TestCancellationToken).AsTask());

        Assert.Contains("No handler found", ex.Message);
    }
}
