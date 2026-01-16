using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_HandlerLifetimeTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private readonly ITestOutputHelper _output = output;

    // Messages for testing different lifetime configurations
    public record SingletonMessage(string Value);
    public record TransientMessage(string Value);
    public record ScopedMessage(string Value);
    public record DefaultLifetimeMessage(string Value);

    // Singleton handler - should be the same instance across requests
    [Handler(Lifetime = HandlerLifetime.Singleton)]
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
    [Handler(Lifetime = HandlerLifetime.Transient)]
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
    [Handler(Lifetime = HandlerLifetime.Scoped)]
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
        var id1 = mediator.Invoke<Guid>(new SingletonMessage("request 1"));
        var id2 = mediator.Invoke<Guid>(new SingletonMessage("request 2"));
        var id3 = mediator.Invoke<Guid>(new SingletonMessage("request 3"));

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
        var id1 = mediator.Invoke<Guid>(new TransientMessage("request 1"));
        var id2 = mediator.Invoke<Guid>(new TransientMessage("request 2"));
        var id3 = mediator.Invoke<Guid>(new TransientMessage("request 3"));

        // All should return different instance IDs
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
        _output.WriteLine($"Transient handler instance IDs: {id1}, {id2}, {id3}");
    }

    [Fact]
    public async Task ScopedHandler_DifferentInstancePerInvocation()
    {
        // Note: The mediator creates its own internal scope for each invocation via ScopedMediator.
        // Scoped handlers get a new scope per invocation, which means different instances.
        // This is by design - the mediator manages its own scoping for handler resolution.

        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<ScopedMessage>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Each invocation creates a new scope, so scoped handlers get new instances
        var id1 = mediator.Invoke<Guid>(new ScopedMessage("request 1"));
        var id2 = mediator.Invoke<Guid>(new ScopedMessage("request 2"));
        var id3 = mediator.Invoke<Guid>(new ScopedMessage("request 3"));

        // Each invocation has its own scope, so different instances
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);

        _output.WriteLine($"Scoped handler instance IDs: {id1}, {id2}, {id3}");
    }

    [Fact]
    public async Task DefaultLifetimeHandler_UsesProjectDefault()
    {
        // The test project has MediatorDefaultHandlerLifetime=Scoped
        // So DefaultLifetimeHandler should behave like ScopedHandler (different instance per invocation)

        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<DefaultLifetimeMessage>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Each invocation creates a new scope, so scoped handlers get new instances
        var id1 = mediator.Invoke<Guid>(new DefaultLifetimeMessage("request 1"));
        var id2 = mediator.Invoke<Guid>(new DefaultLifetimeMessage("request 2"));
        var id3 = mediator.Invoke<Guid>(new DefaultLifetimeMessage("request 3"));

        // Each invocation has its own scope, so different instances (default is Scoped)
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);

        _output.WriteLine($"Default lifetime handler instance IDs: {id1}, {id2}, {id3}");
    }
}
