using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class DiagnosticTest : TestWithLoggingBase
{
    public DiagnosticTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void CheckServiceRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        
        // Let's see what AddMediator registers
        services.AddMediator();
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Check what services are registered
        var registeredServices = services.ToList();
        
        _logger.LogInformation("=== REGISTERED SERVICES ===");
        foreach (var service in registeredServices)
        {
            _logger.LogInformation("Service: {ServiceType} -> {ImplementationType}", 
                service.ServiceType.Name, 
                service.ImplementationType?.Name ?? service.ImplementationInstance?.GetType().Name ?? "Factory");
        }
        
        // Check if mediator is available
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        Assert.NotNull(mediator);
        
        _logger.LogInformation("Mediator type: {MediatorType}", mediator.GetType().Name);
    }
}

// Simple handler for diagnosis
public class DiagnosticTestHandler
{
    public async Task HandleAsync(DiagnosticTestMessage message, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
    }
}

public record DiagnosticTestMessage(string Value);
