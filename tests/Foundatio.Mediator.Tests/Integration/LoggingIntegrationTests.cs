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
        var logMessages = new List<string>();
        
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            builder.AddProvider(new TestLoggerProvider(logMessages));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        services.AddMediator();
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.InvokeAsync(new TestMessage("Hello"));

        // Assert
        _output.WriteLine($"Log messages captured: {logMessages.Count}");
        foreach (var logMessage in logMessages)
        {
            _output.WriteLine(logMessage);
        }
        
        Assert.Contains(logMessages, m => m.Contains("Processing message") && m.Contains("TestMessage"));
        Assert.Contains(logMessages, m => m.Contains("Completed processing message") && m.Contains("TestMessage"));
    }

    public record TestMessage(string Value);

    public class TestMessageHandler
    {
        public string Handle(TestMessage message) => $"Handled: {message.Value}";
    }

    private class TestLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _logMessages;

        public TestLoggerProvider(List<string> logMessages)
        {
            _logMessages = logMessages;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(_logMessages);
        }

        public void Dispose() { }
    }

    private class TestLogger : ILogger
    {
        private readonly List<string> _logMessages;

        public TestLogger(List<string> logMessages)
        {
            _logMessages = logMessages;
        }

        public IDisposable BeginScope<TState>(TState state) => null!;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            _logMessages.Add($"[{logLevel}] {message}");
        }
    }
}