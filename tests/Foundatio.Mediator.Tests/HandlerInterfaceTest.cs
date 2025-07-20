using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class HandlerInterfaceTest : TestWithLoggingBase
{
    public HandlerInterfaceTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task HandlerInterface_ShouldBeRegisteredCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();
        services.AddSingleton<HandlerInterfaceTestService>();
        var serviceProvider = services.BuildServiceProvider();

        var message = new InterfaceTestMessage("Test content");
        
        _logger.LogInformation("=== HANDLER INTERFACE TEST ===");
        _logger.LogInformation("Testing IHandler<TMessage> registration");

        // Act - Verify HandlerRegistration<InterfaceTestMessage> is registered
        var handlerRegistrations = serviceProvider.GetServices<HandlerRegistration<InterfaceTestMessage>>();
        var handlersList = handlerRegistrations.ToList();
        
        _logger.LogInformation("Found {Count} handlers for InterfaceTestMessage", handlersList.Count);

        // Assert
        Assert.Single(handlersList);
        
        // Act - Test the handler
        var handlerRegistration = handlersList.First();
        var handler = handlerRegistration.Handler;
        await handler.HandleAsync<object>(message, default);
        
        var testService = serviceProvider.GetRequiredService<HandlerInterfaceTestService>();
        _logger.LogInformation("Handler executed - CallCount: {Count}", testService.CallCount);
        
        // Assert
        Assert.Equal(1, testService.CallCount);
    }

    [Fact]
    public async Task HandlerInterfaceWithResponse_ShouldBeRegisteredCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();
        services.AddSingleton<HandlerInterfaceTestService>();
        var serviceProvider = services.BuildServiceProvider();

        var query = new InterfaceTestQuery("Test query");
        
        _logger.LogInformation("=== HANDLER INTERFACE WITH RESPONSE TEST ===");
        _logger.LogInformation("Testing IHandler<TMessage, TResponse> registration");

        // Act - Verify HandlerRegistration<InterfaceTestQuery> is registered
        var handlerRegistrations = serviceProvider.GetServices<HandlerRegistration<InterfaceTestQuery>>();
        var handlersList = handlerRegistrations.ToList();
        
        _logger.LogInformation("Found {Count} handlers for InterfaceTestQuery with string response", handlersList.Count);

        // Assert
        Assert.Single(handlersList);
        
        // Act - Test the handler
        var handlerRegistration = handlersList.First();
        var handler = handlerRegistration.Handler;
        var result = await handler.HandleAsync<string>(query, default);
        
        var testService = serviceProvider.GetRequiredService<HandlerInterfaceTestService>();
        _logger.LogInformation("Handler executed - Result: {Result}, CallCount: {Count}", result, testService.CallCount);
        
        // Assert
        Assert.Equal("Response: Test query", result);
        Assert.Equal(1, testService.CallCount);
    }
}

public class HandlerInterfaceTestService
{
    public int CallCount { get; set; }
}

public record InterfaceTestMessage(string Content);
public record InterfaceTestQuery(string Content);

public class InterfaceTestMessageHandler
{
    private readonly HandlerInterfaceTestService _testService;
    private readonly ILogger<InterfaceTestMessageHandler> _logger;

    public InterfaceTestMessageHandler(HandlerInterfaceTestService testService, ILogger<InterfaceTestMessageHandler> logger)
    {
        _testService = testService;
        _logger = logger;
    }

    public async Task HandleAsync(InterfaceTestMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("InterfaceTestMessageHandler.HandleAsync called with: {Content}", message.Content);
        _testService.CallCount++;
        await Task.CompletedTask;
    }
}

public class InterfaceTestQueryHandler
{
    private readonly HandlerInterfaceTestService _testService;
    private readonly ILogger<InterfaceTestQueryHandler> _logger;

    public InterfaceTestQueryHandler(HandlerInterfaceTestService testService, ILogger<InterfaceTestQueryHandler> logger)
    {
        _testService = testService;
        _logger = logger;
    }

    public async Task<string> HandleAsync(InterfaceTestQuery query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("InterfaceTestQueryHandler.HandleAsync called with: {Content}", query.Content);
        _testService.CallCount++;
        await Task.CompletedTask;
        return $"Response: {query.Content}";
    }
}
