using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class AsyncCallSyncHandlerWithAsyncMiddlewareTest : TestWithLoggingBase
{
    public AsyncCallSyncHandlerWithAsyncMiddlewareTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task AsyncInvokeWithSyncHandlerAndAsyncMiddleware_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();

        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act & Assert - this should work without FMED006 warning
        // because AsyncCallSyncHandlerTestMiddleware has async methods
        string result = await mediator.InvokeAsync<string>(new AsyncCallSyncHandlerTestMessage("test"));

        Assert.Equal("Handled: test", result);
    }
}

// Test message
public record AsyncCallSyncHandlerTestMessage(string Value);

// Sync handler
public class AsyncCallSyncHandlerTestHandler
{
    public string Handle(AsyncCallSyncHandlerTestMessage message)
    {
        return $"Handled: {message.Value}";
    }
}

// Middleware with async methods - this makes the pipeline async
public class AsyncCallSyncHandlerTestMiddleware
{
    public object Before(AsyncCallSyncHandlerTestMessage message)
    {
        return "before";
    }

    public async Task After(AsyncCallSyncHandlerTestMessage message, object beforeResult)
    {
        // Simulate some async work
        await Task.Delay(1);
    }
}
