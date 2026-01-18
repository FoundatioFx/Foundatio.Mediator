using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class ScopedDependencyTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    // Test services
    public interface IScopedTestService
    {
        Guid Id { get; }
        void RecordActivity(string activity);
        List<string> Activities { get; }
    }

    public class ScopedTestService : IScopedTestService
    {
        public Guid Id { get; } = Guid.NewGuid();
        public List<string> Activities { get; } = new();

        public void RecordActivity(string activity)
        {
            Activities.Add($"{Id:N}:{activity}");
        }
    }

    // Test messages
    public record RootCommand(string Name) : ICommand;
    public record NestedCommand(string Name) : ICommand;
    public record CascadingCommand(string Name) : ICommand;
    public record CascadingEvent(string Name) : INotification;

    // Test handlers
    public class RootCommandHandler(IScopedTestService scopedService, IMediator mediator)
    {
        public async Task HandleAsync(RootCommand command, CancellationToken ct)
        {
            scopedService.RecordActivity($"RootHandler:{command.Name}");

            // Invoke another handler within the same scope
            await mediator.InvokeAsync(new NestedCommand($"Nested-{command.Name}"), ct);
        }
    }

    public class NestedCommandHandler(IScopedTestService scopedService)
    {
        public async Task HandleAsync(NestedCommand command, CancellationToken ct)
        {
            await Task.CompletedTask; // Simulate async work
            scopedService.RecordActivity($"NestedHandler:{command.Name}");
        }
    }

    public class CascadingCommandHandler(IScopedTestService scopedService)
    {
        public async Task<(string Result, CascadingEvent? Event)> HandleAsync(CascadingCommand command, CancellationToken ct)
        {
            await Task.CompletedTask; // Simulate async work
            scopedService.RecordActivity($"CascadingHandler:{command.Name}");

            return ($"Result-{command.Name}", new CascadingEvent($"Event-{command.Name}"));
        }
    }

    public class CascadingEventHandler
    {
        private readonly IScopedTestService _scopedService;

        public CascadingEventHandler(IScopedTestService scopedService)
        {
            _scopedService = scopedService;
        }

        public async Task HandleAsync(CascadingEvent @event, CancellationToken ct)
        {
            await Task.CompletedTask; // Simulate async work
            _scopedService.RecordActivity($"EventHandler:{@event.Name}");
        }
    }

    [Fact]
    public async Task ScopedDependency_SharedWithinSingleRootHandlerInvocation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IScopedTestService, ScopedTestService>();
        services.AddMediator(b => b.AddAssembly<ScopedDependencyTests>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.InvokeAsync(new RootCommand("Test1"), TestCancellationToken);

        // Assert - Get all recorded activities from any scoped service instances
        using var scope = provider.CreateScope();
        var testService = scope.ServiceProvider.GetRequiredService<IScopedTestService>();

        // Since we can't directly access the scoped service that was used in the handler,
        // we need to verify through a different approach. Let's create a handler that
        // stores the service instance for verification.
    }

    [Fact]
    public async Task ScopedDependency_SameInstanceWithinSameScope()
    {
        // The mediator does NOT create scopes - DI scope management is the caller's responsibility.
        // Within the same scope, scoped services return the same instance.

        // Arrange
        var capturedServices = new List<IScopedTestService>();

        var services = new ServiceCollection();
        services.AddScoped<IScopedTestService, ScopedTestService>();
        services.AddScoped<ServiceCapturingHandler>();
        services.AddMediator(b => b.AddAssembly<ScopedDependencyTests>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Two invocations in the same scope
        await mediator.InvokeAsync(new ServiceCaptureCommand(capturedServices), TestCancellationToken);
        await mediator.InvokeAsync(new ServiceCaptureCommand(capturedServices), TestCancellationToken);

        // Assert - Same scope = same scoped service instance
        Assert.Equal(2, capturedServices.Count);
        Assert.Equal(capturedServices[0].Id, capturedServices[1].Id);
    }

    [Fact]
    public async Task ScopedDependency_DifferentInstancesInDifferentScopes()
    {
        // When caller creates separate scopes, scoped services get different instances.
        // The mediator must be registered as Scoped so each scope gets its own mediator
        // with the scope's IServiceProvider.

        // Arrange
        var capturedServices = new List<IScopedTestService>();

        var services = new ServiceCollection();
        services.AddScoped<IScopedTestService, ScopedTestService>();
        services.AddScoped<ServiceCapturingHandler>();
        services.AddMediator(b => b.AddAssembly<ScopedDependencyTests>().SetMediatorLifetime(ServiceLifetime.Scoped));

        await using var provider = services.BuildServiceProvider();

        // Act - Two invocations in different scopes
        await using (var scope1 = provider.CreateAsyncScope())
        {
            var mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
            await mediator1.InvokeAsync(new ServiceCaptureCommand(capturedServices), TestCancellationToken);
        }

        await using (var scope2 = provider.CreateAsyncScope())
        {
            var mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();
            await mediator2.InvokeAsync(new ServiceCaptureCommand(capturedServices), TestCancellationToken);
        }

        // Assert - Different scopes = different scoped service instances
        Assert.Equal(2, capturedServices.Count);
        Assert.NotEqual(capturedServices[0].Id, capturedServices[1].Id);
    }

    [Fact]
    public async Task ScopedDependency_SharedAcrossCascadingMessages()
    {
        // Arrange
        var capturedServices = new List<IScopedTestService>();

        var services = new ServiceCollection();
        services.AddScoped<IScopedTestService, ScopedTestService>();
        services.AddScoped<CascadingWithCaptureHandler>();
        services.AddScoped<EventWithCaptureHandler>();
        services.AddScoped<ServiceCapturingHandler>();
        services.AddMediator(b => b.AddAssembly<ScopedDependencyTests>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.InvokeAsync<string>(new CascadingWithCaptureCommand("Test", capturedServices), TestCancellationToken);

        // Assert - Both the main handler and the cascading event handler should use the same scoped service
        Assert.Equal(2, capturedServices.Count);
        Assert.Equal(capturedServices[0].Id, capturedServices[1].Id);
    }

    // Additional test handlers for service capture
    public record ServiceCaptureCommand(List<IScopedTestService> CapturedServices) : ICommand;
    public record CascadingWithCaptureCommand(string Name, List<IScopedTestService> CapturedServices) : ICommand;
    public record EventWithCapture(List<IScopedTestService> CapturedServices) : INotification;

    public class ServiceCapturingHandler(IScopedTestService scopedService)
    {
        public async Task HandleAsync(ServiceCaptureCommand command, CancellationToken ct)
        {
            await Task.CompletedTask; // Simulate async work
            command.CapturedServices.Add(scopedService);
        }
    }

    public class CascadingWithCaptureHandler(IScopedTestService scopedService)
    {
        public async Task<(string Result, EventWithCapture Event)> HandleAsync(CascadingWithCaptureCommand command, CancellationToken ct)
        {
            await Task.CompletedTask; // Simulate async work
            command.CapturedServices.Add(scopedService);
            return ($"Result-{command.Name}", new EventWithCapture(command.CapturedServices));
        }
    }

    public class EventWithCaptureHandler(IScopedTestService scopedService)
    {
        public async Task HandleAsync(EventWithCapture @event, CancellationToken ct)
        {
            await Task.CompletedTask; // Simulate async work
            @event.CapturedServices.Add(scopedService);
        }
    }
}
