using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class SingleHandlerTest : TestWithLoggingBase
{
    public SingleHandlerTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task SinglePublishNotificationHandler_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();
        services.AddSingleton<SingleHandlerTestService>();
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var testService = serviceProvider.GetRequiredService<SingleHandlerTestService>();

        var notification = new PublishNotification("Single Handler Test");
        
        _logger.LogInformation("=== SINGLE HANDLER TEST ===");
        _logger.LogInformation("Testing PublishNotification with single handler");

        // Act
        await mediator.InvokeAsync(notification);

        // Assert
        _logger.LogInformation("Result - CallCount: {Count}, Messages: {Messages}", 
            testService.CallCount, string.Join(", ", testService.Messages));
        
        Assert.Equal(1, testService.CallCount);
        Assert.Contains("SingleHandler: Single Handler Test", testService.Messages);
    }
}

public class SingleHandlerTestService
{
    public int CallCount { get; set; }
    public List<string> Messages { get; } = new();

    public void AddMessage(string message)
    {
        CallCount++;
        Messages.Add(message);
    }
}

// Single handler for PublishNotification to test if the issue is multiple handlers
public class SinglePublishNotificationHandler
{
    public async Task HandleAsync(PublishNotification notification, SingleHandlerTestService testService, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        testService.AddMessage($"SingleHandler: {notification.Message}");
    }
}
