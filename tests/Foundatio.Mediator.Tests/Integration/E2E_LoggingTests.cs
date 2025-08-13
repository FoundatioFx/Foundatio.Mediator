using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_LoggingTests
{
    private readonly ITestOutputHelper _output;

    public E2E_LoggingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public record TestMessage(string Message) : IQuery;

    public class TestMessageHandler
    {
        public Task<string> HandleAsync(TestMessage message, CancellationToken ct) => Task.FromResult(message.Message + " Handled");
    }

    [Fact]
    public async Task Mediator_WithNoLogger_DoesNotThrow()
    {
        // Arrange: Create mediator without logger service registered
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<TestMessageHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert: Should not throw even without logger
        var result = await mediator.InvokeAsync<string>(new TestMessage("Test"));
        Assert.Equal("Test Handled", result);
    }

    [Fact]
    public async Task Mediator_WithLogger_ExecutesSuccessfully()
    {
        // Arrange: Create mediator with logger service registered
        var loggedMessages = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new TestLoggerProvider(loggedMessages));
        });
        services.AddMediator(b => b.AddAssembly<TestMessageHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.InvokeAsync<string>(new TestMessage("Test"));

        // Assert
        Assert.Equal("Test Handled", result);
        Assert.Contains(loggedMessages, msg => msg.Contains("Invoking handler for message type TestMessage with expected response type String"));
    }

    private class TestLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _loggedMessages;

        public TestLoggerProvider(List<string> loggedMessages)
        {
            _loggedMessages = loggedMessages;
        }

        public ILogger CreateLogger(string categoryName) => new TestLogger(_loggedMessages);

        public void Dispose() { }
    }

    private class TestLogger : ILogger
    {
        private readonly List<string> _loggedMessages;

        public TestLogger(List<string> loggedMessages)
        {
            _loggedMessages = loggedMessages;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _loggedMessages.Add(formatter(state, exception));
            }
        }
    }
}