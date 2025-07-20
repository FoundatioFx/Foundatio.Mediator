using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class ServiceRegistrationTest : TestWithLoggingBase
{
    public ServiceRegistrationTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void ServiceRegistration_ShouldRegisterHandlerInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();
        services.AddSingleton<ServiceRegistrationTestService>();
        var serviceProvider = services.BuildServiceProvider();

        _logger.LogInformation("=== SERVICE REGISTRATION TEST ===");
        _logger.LogInformation("Verifying that HandlerRegistration<TMessage> interfaces are registered");

        // Act & Assert - Verify various handler registrations
        
        // Test HandlerRegistration<RegistrationTestMessage> registration
        var messageHandlers = serviceProvider.GetServices<HandlerRegistration<RegistrationTestMessage>>();
        var messageHandlersList = messageHandlers.ToList();
        _logger.LogInformation("Found {Count} handlers for RegistrationTestMessage", messageHandlersList.Count);
        Assert.Single(messageHandlersList);

        // Test HandlerRegistration<RegistrationTestQuery> registration
        var queryHandlers = serviceProvider.GetServices<HandlerRegistration<RegistrationTestQuery>>();
        var queryHandlersList = queryHandlers.ToList();
        _logger.LogInformation("Found {Count} handlers for RegistrationTestQuery with string response", queryHandlersList.Count);
        Assert.Single(queryHandlersList);

        // Test HandlerRegistration<RegistrationTestQueryInt> registration
        var intQueryHandlers = serviceProvider.GetServices<HandlerRegistration<RegistrationTestQueryInt>>();
        var intQueryHandlersList = intQueryHandlers.ToList();
        _logger.LogInformation("Found {Count} handlers for RegistrationTestQueryInt with int response", intQueryHandlersList.Count);
        Assert.Single(intQueryHandlersList);

        _logger.LogInformation("All handler interfaces are properly registered!");
    }

    [Fact]
    public async Task ServiceRegistration_ShouldWorkThroughMediator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();
        services.AddSingleton<ServiceRegistrationTestService>();
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var testService = serviceProvider.GetRequiredService<ServiceRegistrationTestService>();

        _logger.LogInformation("=== MEDIATOR INTEGRATION TEST ===");
        _logger.LogInformation("Testing that mediator uses the registered handler interfaces");

        // Act & Assert - Test void handler
        var message = new RegistrationTestMessage("Test message");
        await mediator.InvokeAsync(message);
        Assert.Equal(1, testService.CallCount);
        _logger.LogInformation("Void handler called successfully");

        // Act & Assert - Test handler with string return
        var query = new RegistrationTestQuery("Test query");
        var result = await mediator.InvokeAsync<string>(query);
        Assert.Equal("Response: Test query", result);
        Assert.Equal(2, testService.CallCount);
        _logger.LogInformation("String handler called successfully, returned: {Result}", result);

        // Act & Assert - Test handler with int return
        var intQuery = new RegistrationTestQueryInt(42);
        var intResult = await mediator.InvokeAsync<int>(intQuery);
        Assert.Equal(84, intResult);
        Assert.Equal(3, testService.CallCount);
        _logger.LogInformation("Int handler called successfully, returned: {Result}", intResult);

        _logger.LogInformation("All mediator calls worked through the handler interfaces!");
    }
}

public class ServiceRegistrationTestService
{
    public int CallCount { get; set; }
}

public record RegistrationTestMessage(string Content);
public record RegistrationTestQuery(string Content);
public record RegistrationTestQueryInt(int Value);

public class RegistrationTestMessageHandler
{
    private readonly ServiceRegistrationTestService _testService;
    private readonly ILogger<RegistrationTestMessageHandler> _logger;

    public RegistrationTestMessageHandler(ServiceRegistrationTestService testService, ILogger<RegistrationTestMessageHandler> logger)
    {
        _testService = testService;
        _logger = logger;
    }

    public async Task HandleAsync(RegistrationTestMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RegistrationTestMessageHandler.HandleAsync called with: {Content}", message.Content);
        _testService.CallCount++;
        await Task.CompletedTask;
    }
}

public class RegistrationTestQueryHandler
{
    private readonly ServiceRegistrationTestService _testService;
    private readonly ILogger<RegistrationTestQueryHandler> _logger;

    public RegistrationTestQueryHandler(ServiceRegistrationTestService testService, ILogger<RegistrationTestQueryHandler> logger)
    {
        _testService = testService;
        _logger = logger;
    }

    public async Task<string> HandleAsync(RegistrationTestQuery query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RegistrationTestQueryHandler.HandleAsync called with: {Content}", query.Content);
        _testService.CallCount++;
        await Task.CompletedTask;
        return $"Response: {query.Content}";
    }
}

public class RegistrationTestQueryIntHandler
{
    private readonly ServiceRegistrationTestService _testService;
    private readonly ILogger<RegistrationTestQueryIntHandler> _logger;

    public RegistrationTestQueryIntHandler(ServiceRegistrationTestService testService, ILogger<RegistrationTestQueryIntHandler> logger)
    {
        _testService = testService;
        _logger = logger;
    }

    public async Task<int> HandleAsync(RegistrationTestQueryInt query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RegistrationTestQueryIntHandler.HandleAsync called with: {Value}", query.Value);
        _testService.CallCount++;
        await Task.CompletedTask;
        return query.Value * 2;
    }
}
