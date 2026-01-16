using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_MiddlewareLifetimeTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private readonly ITestOutputHelper _output = output;

    // Message for testing middleware lifetime
    public record MiddlewareLifetimeTestMessage(string Value);

    // Simple handler for the message
    public class MiddlewareLifetimeTestHandler(ILogger<MiddlewareLifetimeTestHandler> logger)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();

        public string Handle(MiddlewareLifetimeTestMessage msg)
        {
            logger.LogInformation("MiddlewareLifetimeTestHandler instance {Id} handling message: {Value}", InstanceId, msg.Value);
            return $"Handled: {msg.Value}";
        }
    }

    // Singleton middleware - should be the same instance across requests
    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class SingletonMiddleware(ILogger<SingletonMiddleware> logger)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public static List<Guid> InstanceIds { get; } = [];

        public void Before(MiddlewareLifetimeTestMessage msg)
        {
            lock (InstanceIds)
            {
                InstanceIds.Add(InstanceId);
            }
            logger.LogInformation("SingletonMiddleware instance {Id} before handling message: {Value}", InstanceId, msg.Value);
        }
    }

    // Transient middleware - should be a new instance for each request
    [Middleware(Lifetime = MediatorLifetime.Transient)]
    public class TransientMiddleware(ILogger<TransientMiddleware> logger)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public static List<Guid> InstanceIds { get; } = [];

        public void Before(MiddlewareLifetimeTestMessage msg)
        {
            lock (InstanceIds)
            {
                InstanceIds.Add(InstanceId);
            }
            logger.LogInformation("TransientMiddleware instance {Id} before handling message: {Value}", InstanceId, msg.Value);
        }
    }

    // Scoped middleware - should be the same instance within a scope
    [Middleware(Lifetime = MediatorLifetime.Scoped)]
    public class ScopedMiddleware(ILogger<ScopedMiddleware> logger)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public static List<Guid> InstanceIds { get; } = [];

        public void Before(MiddlewareLifetimeTestMessage msg)
        {
            lock (InstanceIds)
            {
                InstanceIds.Add(InstanceId);
            }
            logger.LogInformation("ScopedMiddleware instance {Id} before handling message: {Value}", InstanceId, msg.Value);
        }
    }

    [Fact]
    public async Task SingletonMiddleware_SameInstanceAcrossRequests()
    {
        // Clear static state
        SingletonMiddleware.InstanceIds.Clear();

        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<MiddlewareLifetimeTestMessage>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Make multiple requests
        mediator.Invoke<string>(new MiddlewareLifetimeTestMessage("request 1"), TestContext.Current.CancellationToken);
        mediator.Invoke<string>(new MiddlewareLifetimeTestMessage("request 2"), TestContext.Current.CancellationToken);
        mediator.Invoke<string>(new MiddlewareLifetimeTestMessage("request 3"), TestContext.Current.CancellationToken);

        // All should use the same middleware instance
        Assert.Equal(3, SingletonMiddleware.InstanceIds.Count);
        Assert.Single(SingletonMiddleware.InstanceIds.Distinct());
        _output.WriteLine($"Singleton middleware instance IDs: {string.Join(", ", SingletonMiddleware.InstanceIds)}");
    }

    [Fact]
    public async Task TransientMiddleware_NewInstanceEachRequest()
    {
        // Clear static state
        TransientMiddleware.InstanceIds.Clear();

        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<MiddlewareLifetimeTestMessage>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Make multiple requests
        mediator.Invoke<string>(new MiddlewareLifetimeTestMessage("request 1"), TestContext.Current.CancellationToken);
        mediator.Invoke<string>(new MiddlewareLifetimeTestMessage("request 2"), TestContext.Current.CancellationToken);
        mediator.Invoke<string>(new MiddlewareLifetimeTestMessage("request 3"), TestContext.Current.CancellationToken);

        // Each should use a different middleware instance
        Assert.Equal(3, TransientMiddleware.InstanceIds.Count);
        Assert.Equal(3, TransientMiddleware.InstanceIds.Distinct().Count());
        _output.WriteLine($"Transient middleware instance IDs: {string.Join(", ", TransientMiddleware.InstanceIds)}");
    }

    [Fact]
    public async Task ScopedMiddleware_DifferentInstancePerInvocation()
    {
        // Note: The mediator creates its own internal scope for each invocation via ScopedMediator.
        // Scoped middleware gets a new scope per invocation, which means different instances.
        // This is by design - the mediator manages its own scoping for middleware resolution.

        // Clear static state
        ScopedMiddleware.InstanceIds.Clear();

        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<MiddlewareLifetimeTestMessage>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Each invocation creates a new scope, so scoped middleware gets new instances
        mediator.Invoke<string>(new MiddlewareLifetimeTestMessage("request 1"), TestContext.Current.CancellationToken);
        mediator.Invoke<string>(new MiddlewareLifetimeTestMessage("request 2"), TestContext.Current.CancellationToken);
        mediator.Invoke<string>(new MiddlewareLifetimeTestMessage("request 3"), TestContext.Current.CancellationToken);

        // Each invocation has its own scope, so different instances
        Assert.Equal(3, ScopedMiddleware.InstanceIds.Count);
        Assert.Equal(3, ScopedMiddleware.InstanceIds.Distinct().Count());
        _output.WriteLine($"Scoped middleware instance IDs: {string.Join(", ", ScopedMiddleware.InstanceIds)}");
    }
}
