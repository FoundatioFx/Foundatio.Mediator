using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_MediatorLifetimeTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private readonly ITestOutputHelper _output = output;

    // Messages for testing different lifetime configurations
    public record SingletonMessage(string Value);
    public record TransientMessage(string Value);
    public record ScopedMessage(string Value);
    public record DefaultLifetimeMessage(string Value);

    // Singleton handler - should be the same instance across requests
    [Handler(Lifetime = MediatorLifetime.Singleton)]
    public class SingletonHandler(ILogger<SingletonHandler> logger)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();

        public Guid Handle(SingletonMessage msg)
        {
            logger.LogInformation("SingletonHandler instance {Id} handling message: {Value}", InstanceId, msg.Value);
            return InstanceId;
        }
    }

    // Transient handler - should be a new instance for each request
    [Handler(Lifetime = MediatorLifetime.Transient)]
    public class TransientHandler(ILogger<TransientHandler> logger)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();

        public Guid Handle(TransientMessage msg)
        {
            logger.LogInformation("TransientHandler instance {Id} handling message: {Value}", InstanceId, msg.Value);
            return InstanceId;
        }
    }

    // Scoped handler - should be the same instance within a scope
    [Handler(Lifetime = MediatorLifetime.Scoped)]
    public class ScopedHandler(ILogger<ScopedHandler> logger)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();

        public Guid Handle(ScopedMessage msg)
        {
            logger.LogInformation("ScopedHandler instance {Id} handling message: {Value}", InstanceId, msg.Value);
            return InstanceId;
        }
    }

    // Handler without explicit lifetime - uses project default (Scoped in test project)
    public class DefaultLifetimeHandler(ILogger<DefaultLifetimeHandler> logger)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();

        public Guid Handle(DefaultLifetimeMessage msg)
        {
            logger.LogInformation("DefaultLifetimeHandler instance {Id} handling message: {Value}", InstanceId, msg.Value);
            return InstanceId;
        }
    }

    [Fact]
    public async Task SingletonHandler_SameInstanceAcrossRequests()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<SingletonMessage>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Make multiple requests
        var id1 = mediator.Invoke<Guid>(new SingletonMessage("request 1"), TestContext.Current.CancellationToken);
        var id2 = mediator.Invoke<Guid>(new SingletonMessage("request 2"), TestContext.Current.CancellationToken);
        var id3 = mediator.Invoke<Guid>(new SingletonMessage("request 3"), TestContext.Current.CancellationToken);

        // All should return the same instance ID
        Assert.Equal(id1, id2);
        Assert.Equal(id2, id3);
        _output.WriteLine($"Singleton handler instance ID: {id1}");
    }

    [Fact]
    public async Task TransientHandler_NewInstanceEachRequest()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TransientMessage>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Make multiple requests
        var id1 = mediator.Invoke<Guid>(new TransientMessage("request 1"), TestContext.Current.CancellationToken);
        var id2 = mediator.Invoke<Guid>(new TransientMessage("request 2"), TestContext.Current.CancellationToken);
        var id3 = mediator.Invoke<Guid>(new TransientMessage("request 3"), TestContext.Current.CancellationToken);

        // All should return different instance IDs
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
        _output.WriteLine($"Transient handler instance IDs: {id1}, {id2}, {id3}");
    }

    [Fact]
    public async Task ScopedHandler_SameInstanceWithinSameScope()
    {
        // The mediator does NOT create scopes - DI scope management is the caller's responsibility.
        // When invoked with the same service provider scope, scoped handlers return the same instance.

        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<ScopedMessage>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Same scope = same scoped handler instance
        var id1 = mediator.Invoke<Guid>(new ScopedMessage("request 1"), TestContext.Current.CancellationToken);
        var id2 = mediator.Invoke<Guid>(new ScopedMessage("request 2"), TestContext.Current.CancellationToken);
        var id3 = mediator.Invoke<Guid>(new ScopedMessage("request 3"), TestContext.Current.CancellationToken);

        // Same scope = same instance
        Assert.Equal(id1, id2);
        Assert.Equal(id2, id3);

        _output.WriteLine($"Scoped handler instance IDs (same scope): {id1}, {id2}, {id3}");
    }

    [Fact]
    public async Task ScopedHandler_DifferentInstanceInDifferentScopes()
    {
        // When caller creates separate scopes, scoped handlers get different instances.
        // The mediator must be registered as Scoped so each scope gets its own mediator
        // with the scope's IServiceProvider.

        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<ScopedMessage>().SetMediatorLifetime(ServiceLifetime.Scoped));

        await using var provider = services.BuildServiceProvider();

        // Each scope gets its own mediator and scoped handler instance
        Guid id1, id2, id3;

        await using (var scope1 = provider.CreateAsyncScope())
        {
            var mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
            id1 = mediator1.Invoke<Guid>(new ScopedMessage("request 1"), TestContext.Current.CancellationToken);
        }

        await using (var scope2 = provider.CreateAsyncScope())
        {
            var mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();
            id2 = mediator2.Invoke<Guid>(new ScopedMessage("request 2"), TestContext.Current.CancellationToken);
        }

        await using (var scope3 = provider.CreateAsyncScope())
        {
            var mediator3 = scope3.ServiceProvider.GetRequiredService<IMediator>();
            id3 = mediator3.Invoke<Guid>(new ScopedMessage("request 3"), TestContext.Current.CancellationToken);
        }

        // Different scopes = different instances
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);

        _output.WriteLine($"Scoped handler instance IDs (different scopes): {id1}, {id2}, {id3}");
    }

    [Fact]
    public async Task DefaultLifetimeHandler_UsesProjectDefault()
    {
        // The test project has MediatorDefaultHandlerLifetime=Scoped
        // So DefaultLifetimeHandler behaves like ScopedHandler - same instance within same scope

        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<DefaultLifetimeMessage>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Same scope = same scoped handler instance
        var id1 = mediator.Invoke<Guid>(new DefaultLifetimeMessage("request 1"), TestContext.Current.CancellationToken);
        var id2 = mediator.Invoke<Guid>(new DefaultLifetimeMessage("request 2"), TestContext.Current.CancellationToken);
        var id3 = mediator.Invoke<Guid>(new DefaultLifetimeMessage("request 3"), TestContext.Current.CancellationToken);

        // Same scope = same instance (default is Scoped per project setting)
        Assert.Equal(id1, id2);
        Assert.Equal(id2, id3);

        _output.WriteLine($"Default lifetime handler instance IDs: {id1}, {id2}, {id3}");
    }
}
