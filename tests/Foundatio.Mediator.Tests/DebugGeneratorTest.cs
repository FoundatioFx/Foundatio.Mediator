using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class DebugGeneratorTest : TestWithLoggingBase
{
    public DebugGeneratorTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task TestBasicMediator_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        var simpleNotification = new SimpleTestNotification("Debug Basic");
        
        _logger.LogInformation("=== BASIC MEDIATOR TEST ===");
        _logger.LogInformation("Testing SimpleTestNotification");

        // Act & Assert
        await mediator.InvokeAsync(simpleNotification);
        _logger.LogInformation("Basic test completed successfully");
    }

    [Fact]
    public void TestUniqueMessage_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();
        services.AddSingleton<DebugTestService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var testService = serviceProvider.GetRequiredService<DebugTestService>();

        var uniqueMessage = new DebugUniqueMessage("Debug Unique");
        
        _logger.LogInformation("=== UNIQUE MESSAGE TEST ===");
        _logger.LogInformation("Testing DebugUniqueMessage");

        // Act
        mediator.Invoke(uniqueMessage);

        // Assert
        _logger.LogInformation("Result - CallCount: {Count}, Messages: {Messages}", 
            testService.CallCount, String.Join(", ", testService.Messages));
        
        Assert.Equal(1, testService.CallCount);
        Assert.Contains("DebugUnique: Debug Unique", testService.Messages);
    }
}

// New unique message type with no conflicts
public record DebugUniqueMessage(string Message);

// Simple test service
public class DebugTestService
{
    public int CallCount { get; set; }
    public List<string> Messages { get; } = new();

    public void AddMessage(string message)
    {
        CallCount++;
        Messages.Add(message);
    }
}

// Single handler for the unique message
public class DebugUniqueMessageHandler
{
    public void Handle(DebugUniqueMessage message, DebugTestService testService)
    {
        testService.AddMessage($"DebugUnique: {message.Message}");
    }
}
