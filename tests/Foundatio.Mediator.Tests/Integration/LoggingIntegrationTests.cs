using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests.Integration;

public class LoggingIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public LoggingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Handler_Should_Log_Debug_Messages()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging(b => 
            {
                b.SetMinimumLevel(LogLevel.Debug);
                b.AddTestLogger(_output);
            })
            .AddMediator();
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var testLogger = serviceProvider.GetRequiredService<TestLogger>();

        // Act
        await mediator.InvokeAsync(new TestMessage("Hello"));

        // Assert
        Assert.True(testLogger.LogEntries.Count > 0, "Expected log entries but found none");
        
        var messageExists = testLogger.LogEntries.Any(entry => 
            entry.LogLevel == LogLevel.Debug && 
            entry.Message.Contains("Processing message"));
        Assert.True(messageExists, "Expected to find 'Processing message' debug log");
    }

    public record TestMessage(string Value);

    public class TestMessageHandler
    {
        public string Handle(TestMessage message) => $"Handled: {message.Value}";
    }
}