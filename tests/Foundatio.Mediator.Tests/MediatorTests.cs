using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Foundatio.Mediator.Tests;

public class MediatorTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMediator _mediator;

    public MediatorTests()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task InvokeAsync_WithVoidHandler_ShouldExecuteSuccessfully()
    {
        // Arrange
        var command = new TestCommand("test");

        // Act & Assert (should not throw)
        await _mediator.InvokeAsync(command);
    }

    [Fact]
    public async Task InvokeAsync_WithGenericReturnType_ShouldReturnCorrectValue()
    {
        // Arrange
        var query = new TestQuery("World");

        // Act
        var result = await _mediator.InvokeAsync<string>(query);

        // Assert
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public async Task InvokeAsync_WithPublishNotification_ShouldWork()
    {
        // Act & Assert (should not throw)
        var notification = new TestNotification("Test");
        await _mediator.InvokeAsync(notification);
    }
}

// Test messages
public record TestCommand(string Message);
public record TestQuery(string Name);

// Test handlers - separate classes
public class TestCommandHandler
{
    public async Task HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        // Handler executed successfully
    }
}

public class TestQueryHandler
{
    public async Task<string> HandleAsync(TestQuery query, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return $"Hello, {query.Name}!";
    }
}

// Test if this gets discovered
public record TestNotification(string Message);

public class TestNotificationHandler
{
    public async Task HandleAsync(TestNotification notification, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
    }
}
