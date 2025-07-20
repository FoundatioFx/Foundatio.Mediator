using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Foundatio.Mediator.Tests;

public class DependencyInjectionTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMediator _mediator;

    public DependencyInjectionTests()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        
        // Add test services
        services.AddSingleton<ITestService, TestService>();
        services.AddScoped<IScopedService, ScopedService>();
        
        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handler_WithMultipleDependencies_ShouldInjectCorrectly()
    {
        // Arrange
        var command = new TestCommandWithDependencies("test data");

        // Act & Assert (should not throw)
        await _mediator.InvokeAsync(command);
        
        // Verify the services were called
        var testService = _serviceProvider.GetRequiredService<ITestService>();
        var scopedService = _serviceProvider.GetRequiredService<IScopedService>();
        
        Assert.Equal(1, testService.CallCount);
        Assert.Equal(1, scopedService.CallCount);
    }

    [Fact]
    public async Task Handler_WithReturnValueAndDependencies_ShouldWork()
    {
        // Arrange
        var query = new TestQueryWithDependencies("input");

        // Act
        var result = await _mediator.InvokeAsync<string>(query);

        // Assert
        Assert.Equal("Processed: input", result);
        
        var testService = _serviceProvider.GetRequiredService<ITestService>();
        Assert.Equal(1, testService.CallCount);
    }

    [Fact]
    public async Task Handler_WithOnlyMessageParameter_ShouldStillWork()
    {
        // Arrange
        var command = new SimpleTestCommand("simple");

        // Act & Assert (should not throw)
        await _mediator.InvokeAsync(command);
    }
}

// Test messages
public record TestCommandWithDependencies(string Data);
public record TestQueryWithDependencies(string Input);
public record SimpleTestCommand(string Value);

// Test services
public interface ITestService
{
    void DoSomething();
    int CallCount { get; }
}

public class TestService : ITestService
{
    public int CallCount { get; private set; }
    
    public void DoSomething()
    {
        CallCount++;
    }
}

public interface IScopedService
{
    void Process();
    int CallCount { get; }
}

public class ScopedService : IScopedService
{
    public int CallCount { get; private set; }
    
    public void Process()
    {
        CallCount++;
    }
}

// Test handlers with various dependency injection patterns
public class TestCommandWithDependenciesHandler
{
    public async Task HandleAsync(
        TestCommandWithDependencies command,
        ITestService testService,
        IScopedService scopedService,
        CancellationToken cancellationToken = default)
    {
        testService.DoSomething();
        scopedService.Process();
        await Task.CompletedTask;
    }
}

public class TestQueryWithDependenciesHandler
{
    public async Task<string> HandleAsync(
        TestQueryWithDependencies query,
        ITestService testService,
        CancellationToken cancellationToken = default)
    {
        testService.DoSomething();
        await Task.CompletedTask;
        return $"Processed: {query.Input}";
    }
}

public class SimpleTestCommandHandler
{
    public async Task HandleAsync(SimpleTestCommand command, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        // Simple handler without any dependencies except the message and cancellation token
    }
}
