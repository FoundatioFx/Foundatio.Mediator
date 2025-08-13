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

    public record TestNotification(string Message) : INotification;

    public class TestNotificationHandler1
    {
        public Task HandleAsync(TestNotification notification, CancellationToken ct) => Task.CompletedTask;
    }

    public class TestNotificationHandler2
    {
        public Task HandleAsync(TestNotification notification, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task InvokeAsync_LogsMessageProcessing()
    {
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

        var result = await mediator.InvokeAsync<string>(new TestMessage("Test"));

        Assert.Equal("Test Handled", result);
        Assert.Contains(loggedMessages, msg => msg.Contains("Invoking handler for message type TestMessage with expected response type String"));
    }

    [Fact]
    public async Task PublishAsync_LogsMessageProcessing()
    {
        var loggedMessages = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new TestLoggerProvider(loggedMessages));
        });
        services.AddMediator(b => b.AddAssembly<TestNotificationHandler1>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.PublishAsync(new TestNotification("Test"));

        Assert.Contains(loggedMessages, msg => msg.Contains("Publishing message type TestNotification to") && msg.Contains("handlers"));
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

        public IDisposable BeginScope<TState>(TState state) => null!;

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