using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

// Test file for call site validation
namespace Foundatio.Mediator.Tests;

public class CallSiteValidationTest : TestWithLoggingBase
{
    public CallSiteValidationTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Should_Validate_Single_Handler_Invoke_Successfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();

        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        var command = new CallSiteTestCommand("Test Message");
        _logger.LogInformation("Testing InvokeAsync with single handler for message: {Message}", command.Message);

        // Act & Assert - This should compile and work since there's only one handler
        string result = await mediator.InvokeAsync<string>(command);

        _logger.LogInformation("InvokeAsync completed successfully with result: {Result}", result);
        Assert.Equal("Processed: Test Message", result);
    }

    [Fact]
    public async Task Should_Allow_Multiple_Handlers_For_Publish()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();
        services.AddSingleton<CallSiteTestService>();

        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var testService = serviceProvider.GetRequiredService<CallSiteTestService>();

        var notification = new CallSiteTestNotification("Broadcast Message");
        _logger.LogInformation("Testing PublishAsync with multiple handlers for message: {Message}", notification.Message);

        // Act - This should work fine since Publish allows multiple handlers
        await mediator.PublishAsync(notification);

        // Assert
        _logger.LogInformation("PublishAsync completed. Handler call count: {CallCount}", testService.CallCount);
        Assert.Equal(2, testService.CallCount); // Both handlers should be called
        Assert.Contains("Handler1: Broadcast Message", testService.Messages);
        Assert.Contains("Handler2: Broadcast Message", testService.Messages);
    }
}

// Test message types and handlers
public record CallSiteTestCommand(string Message);
public record CallSiteTestNotification(string Message);

// Single handler for CallSiteTestCommand (allows Invoke)
public class CallSiteTestCommandHandler
{
    private readonly ILogger<CallSiteTestCommandHandler> _logger;

    public CallSiteTestCommandHandler(ILogger<CallSiteTestCommandHandler> logger)
    {
        _logger = logger;
    }

    public async Task<string> HandleAsync(CallSiteTestCommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing command: {Message}", command.Message);
        await Task.Delay(10, cancellationToken);
        return $"Processed: {command.Message}";
    }
}

// Multiple handlers for CallSiteTestNotification (allows Publish)
public class CallSiteTestNotification1Handler
{
    private readonly ILogger<CallSiteTestNotification1Handler> _logger;

    public CallSiteTestNotification1Handler(ILogger<CallSiteTestNotification1Handler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(CallSiteTestNotification notification, CallSiteTestService testService, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handler1 processing notification: {Message}", notification.Message);
        await Task.Delay(5, cancellationToken);
        testService.AddMessage($"Handler1: {notification.Message}");
    }
}

public class CallSiteTestNotification2Handler
{
    private readonly ILogger<CallSiteTestNotification2Handler> _logger;

    public CallSiteTestNotification2Handler(ILogger<CallSiteTestNotification2Handler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(CallSiteTestNotification notification, CallSiteTestService testService, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handler2 processing notification: {Message}", notification.Message);
        await Task.Delay(5, cancellationToken);
        testService.AddMessage($"Handler2: {notification.Message}");
    }
}

public class CallSiteTestService
{
    private readonly List<string> _messages = new();
    private int _callCount = 0;

    public IReadOnlyList<string> Messages => _messages.AsReadOnly();
    public int CallCount => _callCount;

    public void AddMessage(string message)
    {
        _messages.Add(message);
        Interlocked.Increment(ref _callCount);
    }
}
