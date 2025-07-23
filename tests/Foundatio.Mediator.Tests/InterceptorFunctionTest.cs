using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class InterceptorFunctionTest : TestWithLoggingBase
{
    public InterceptorFunctionTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Should_Use_Interceptor_For_Invoke_Calls()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();

        // Add a tracking service to verify call patterns
        services.AddSingleton<InterceptorTestTracker>();

        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var tracker = serviceProvider.GetRequiredService<InterceptorTestTracker>();

        var command = new InterceptorTestCommand("Test Interceptor");
        _logger.LogInformation("Testing interceptor functionality for command: {Message}", command.Message);

        // Act - This should be intercepted and call the static handler directly
        string result = await mediator.InvokeAsync<string>(command);

        // Assert
        _logger.LogInformation("InvokeAsync completed with result: {Result}", result);
        Assert.Equal("Intercepted: Test Interceptor", result);

        // Verify the tracking shows static method was called
        Assert.True(tracker.WasStaticMethodCalled, "Static handler method should have been called via interceptor");
        _logger.LogInformation("✓ Interceptor successfully called static handler method");
    }
}

// Test message and handler for interceptor testing
public record InterceptorTestCommand(string Message);

public class InterceptorTestCommandHandler
{
    private readonly ILogger<InterceptorTestCommandHandler> _logger;
    private readonly InterceptorTestTracker _tracker;

    public InterceptorTestCommandHandler(ILogger<InterceptorTestCommandHandler> logger, InterceptorTestTracker tracker)
    {
        _logger = logger;
        _tracker = tracker;
    }

    public async Task<string> HandleAsync(InterceptorTestCommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handler processing command: {Message}", command.Message);
        _tracker.RecordStaticMethodCall();
        await Task.Delay(10, cancellationToken);
        return $"Intercepted: {command.Message}";
    }
}

// Service to track how the handler was called
public class InterceptorTestTracker
{
    public bool WasStaticMethodCalled { get; private set; }

    public void RecordStaticMethodCall()
    {
        WasStaticMethodCalled = true;
    }
}
