using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Tests.Integration;

public class LoggingIntegrationTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void Handler_Invocation_Works_With_Logging_Configured()
    {
        // Arrange: register mediator with a debug-level logger to ensure logging
        // infrastructure doesn't interfere with handler dispatch
        var services = new ServiceCollection();
        services.AddLogging(b =>
            {
                b.SetMinimumLevel(LogLevel.Debug);
                b.AddTestLogger(_output);
            });
        services.AddMediator();

        using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var testLogger = serviceProvider.GetRequiredService<TestLogger>();

        // Act
        var result = mediator.Invoke<string>(new TestMessage("Hello"), TestCancellationToken);

        // Assert: handler executes correctly even with verbose logging configured
        Assert.Equal("Handled: Hello", result);

        // Verify the logger was actually created and reachable (proves DI wiring is correct)
        Assert.NotNull(testLogger);
    }

    [Fact]
    public async Task Handler_InvokeAsync_Works_With_Logging_Configured()
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
            {
                b.SetMinimumLevel(LogLevel.Trace);
                b.AddTestLogger(_output);
            });
        services.AddMediator();

        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        var result = await mediator.InvokeAsync<string>(new TestMessage("World"), TestCancellationToken);

        Assert.Equal("Handled: World", result);
    }

    public record TestMessage(string Value);

    public class TestMessageHandler
    {
        public string Handle(TestMessage message) => $"Handled: {message.Value}";
    }
}
