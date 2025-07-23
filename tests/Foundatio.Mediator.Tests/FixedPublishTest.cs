using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class FixedPublishTest : TestWithLoggingBase
{
    public FixedPublishTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void Publish_WithTwoSyncHandlers_CallsAllHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();
        services.AddSingleton<FixedTestService>();
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var testService = serviceProvider.GetRequiredService<FixedTestService>();

        var command = new FixedSyncCommand("Fixed Sync Test");

        _logger.LogInformation("Starting synchronous Publish test with message: {Message}", command.Message);

        // Act
        mediator.Publish(command);

        // Assert
        _logger.LogInformation("Sync Publish completed. CallCount: {CallCount}, Messages: {Messages}",
            testService.CallCount, String.Join(", ", testService.Messages));

        Assert.Equal(2, testService.CallCount); // Two handlers should be called for FixedSyncCommand
        Assert.Contains("FixedSyncCommand1Handler: Fixed Sync Test", testService.Messages);
        Assert.Contains("FixedSyncCommand2Handler: Fixed Sync Test", testService.Messages);
    }

    [Fact]
    public async Task PublishAsync_WithTwoAsyncHandlers_CallsAllHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();
        services.AddSingleton<FixedTestService>();
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var testService = serviceProvider.GetRequiredService<FixedTestService>();

        var notification = new FixedAsyncNotification("Fixed Async Test");

        _logger.LogInformation("Starting async Publish test with message: {Message}", notification.Message);

        // Act
        await mediator.PublishAsync(notification);

        // Assert
        _logger.LogInformation("Async Publish completed. CallCount: {CallCount}, Messages: {Messages}",
            testService.CallCount, String.Join(", ", testService.Messages));

        Assert.Equal(2, testService.CallCount); // Two handlers should be called for FixedAsyncNotification
        Assert.Contains("FixedAsync1Handler: Fixed Async Test", testService.Messages);
        Assert.Contains("FixedAsync2Handler: Fixed Async Test", testService.Messages);
    }
}

// New message types without conflicts
public record FixedSyncCommand(string Message);
public record FixedAsyncNotification(string Message);

// Simple test service
public class FixedTestService
{
    private int _callCount;
    public int CallCount
    {
        get => _callCount;
        set => _callCount = value;
    }

    public List<string> Messages { get; } = new();

    public void AddMessage(string message)
    {
        System.Threading.Interlocked.Increment(ref _callCount);
        lock (Messages)
        {
            Messages.Add(message);
        }
    }
}

// Two sync handlers for FixedSyncCommand
public class FixedSyncCommand1Handler
{
    private readonly ILogger<FixedSyncCommand1Handler> _logger;

    public FixedSyncCommand1Handler(ILogger<FixedSyncCommand1Handler> logger)
    {
        _logger = logger;
    }

    public void Handle(FixedSyncCommand command, FixedTestService testService)
    {
        _logger.LogInformation("FixedSyncCommand1Handler processing command: {Message}", command.Message);
        testService.AddMessage($"FixedSyncCommand1Handler: {command.Message}");
        _logger.LogInformation("FixedSyncCommand1Handler completed for: {Message}", command.Message);
    }
}

public class FixedSyncCommand2Handler
{
    private readonly ILogger<FixedSyncCommand2Handler> _logger;

    public FixedSyncCommand2Handler(ILogger<FixedSyncCommand2Handler> logger)
    {
        _logger = logger;
    }

    public void Handle(FixedSyncCommand command, FixedTestService testService)
    {
        _logger.LogInformation("FixedSyncCommand2Handler processing command: {Message}", command.Message);
        testService.AddMessage($"FixedSyncCommand2Handler: {command.Message}");
        _logger.LogInformation("FixedSyncCommand2Handler completed for: {Message}", command.Message);
    }
}

// Two async handlers for FixedAsyncNotification
public class FixedAsync1Handler
{
    private readonly ILogger<FixedAsync1Handler> _logger;

    public FixedAsync1Handler(ILogger<FixedAsync1Handler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(FixedAsyncNotification notification, FixedTestService testService, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("FixedAsync1Handler processing notification: {Message}", notification.Message);
        await Task.Delay(10, cancellationToken);
        testService.AddMessage($"FixedAsync1Handler: {notification.Message}");
        _logger.LogInformation("FixedAsync1Handler completed for: {Message}", notification.Message);
    }
}

public class FixedAsync2Handler
{
    private readonly ILogger<FixedAsync2Handler> _logger;

    public FixedAsync2Handler(ILogger<FixedAsync2Handler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(FixedAsyncNotification notification, FixedTestService testService, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("FixedAsync2Handler processing notification: {Message}", notification.Message);
        await Task.Delay(20, cancellationToken);
        testService.AddMessage($"FixedAsync2Handler: {notification.Message}");
        _logger.LogInformation("FixedAsync2Handler completed for: {Message}", notification.Message);
    }
}
