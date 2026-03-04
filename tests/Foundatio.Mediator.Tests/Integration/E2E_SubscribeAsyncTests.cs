using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_SubscribeAsyncTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private readonly ITestOutputHelper _output = output;

    public interface ITestEvent { }
    public record TestEvent(string Name) : ITestEvent;
    public record OtherEvent(string Name) : ITestEvent;
    public record UnrelatedEvent(string Name);

    [Fact]
    public async Task SubscribeAsync_ReceivesConcrete_WhenPublished()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<TestEvent>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cancellationToken: cts.Token))
            {
                received.Add(item);
            }
        });

        // Give subscriber time to register.
        await Task.Delay(50);

        await mediator.PublishAsync(new TestEvent("one"));
        await mediator.PublishAsync(new TestEvent("two"));

        // Give messages time to arrive.
        await Task.Delay(50);
        cts.Cancel();
        await subscriberTask;

        Assert.Equal(2, received.Count);
        Assert.Equal("one", received[0].Name);
        Assert.Equal("two", received[1].Name);
    }

    [Fact]
    public async Task SubscribeAsync_ReceivesInterface_WhenDerivedTypePublished()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<ITestEvent>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<ITestEvent>(cancellationToken: cts.Token))
            {
                received.Add(item);
            }
        });

        await Task.Delay(50);

        await mediator.PublishAsync(new TestEvent("hello"));
        await mediator.PublishAsync(new OtherEvent("world"));

        await Task.Delay(50);
        cts.Cancel();
        await subscriberTask;

        Assert.Equal(2, received.Count);
        Assert.IsType<TestEvent>(received[0]);
        Assert.IsType<OtherEvent>(received[1]);
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotReceive_UnrelatedTypes()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<TestEvent>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cancellationToken: cts.Token))
            {
                received.Add(item);
            }
        });

        await Task.Delay(50);

        // Publish an unrelated type — subscriber should not receive it.
        await mediator.PublishAsync(new UnrelatedEvent("nope"));

        await Task.Delay(50);
        cts.Cancel();
        await subscriberTask;

        Assert.Empty(received);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_AllReceive()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received1 = new List<TestEvent>();
        var received2 = new List<TestEvent>();

        var sub1 = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cancellationToken: cts.Token))
                received1.Add(item);
        });

        var sub2 = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cancellationToken: cts.Token))
                received2.Add(item);
        });

        await Task.Delay(50);

        await mediator.PublishAsync(new TestEvent("shared"));

        await Task.Delay(50);
        cts.Cancel();
        await Task.WhenAll(sub1, sub2);

        Assert.Single(received1);
        Assert.Single(received2);
        Assert.Equal("shared", received1[0].Name);
        Assert.Equal("shared", received2[0].Name);
    }

    [Fact]
    public async Task SubscribeAsync_CancellationEndsStream()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<TestEvent>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cancellationToken: cts.Token))
            {
                received.Add(item);
                if (received.Count == 2)
                    cts.Cancel();
            }
        });

        await Task.Delay(50);

        await mediator.PublishAsync(new TestEvent("a"));
        await mediator.PublishAsync(new TestEvent("b"));
        await mediator.PublishAsync(new TestEvent("c"));

        await subscriberTask;

        // Only the first 2 should be received before cancellation took effect.
        Assert.Equal(2, received.Count);
    }

    [Fact]
    public async Task SubscribeAsync_DropsOldest_WhenBufferFull()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var registry = provider.GetRequiredService<HandlerRegistry>();

        using var cts = new CancellationTokenSource();
        var received = new List<TestEvent>();

        // Use a tiny buffer (capacity 2) so we can verify drop behavior.
        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(maxCapacity: 2, cancellationToken: cts.Token))
            {
                received.Add(item);
            }
        });

        await Task.Delay(50);

        // Publish 5 items rapidly — buffer should drop oldest and keep newest.
        for (int i = 0; i < 5; i++)
            await mediator.PublishAsync(new TestEvent($"msg-{i}"));

        await Task.Delay(100);
        cts.Cancel();
        await subscriberTask;

        // With BoundedChannelFullMode.DropOldest and capacity 2, the subscriber
        // may have read some items while they were being published (draining the buffer),
        // so we can't predict the exact items. But we can verify we got at least something
        // and no more than 5.
        Assert.NotEmpty(received);
        Assert.True(received.Count <= 5);

        // The last published item should always be present since the buffer keeps newest.
        Assert.Contains(received, e => e.Name == "msg-4");
    }

    [Fact]
    public async Task HasSubscribers_ReflectsActiveSubscriptions()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        Assert.False(registry.HasSubscribers);

        using var cts = new CancellationTokenSource();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var _ in mediator.SubscribeAsync<TestEvent>(cancellationToken: cts.Token))
            { }
        });

        await Task.Delay(50);
        Assert.True(registry.HasSubscribers);

        cts.Cancel();
        await subscriberTask;

        // After cancellation removes the subscription.
        await Task.Delay(50);
        Assert.False(registry.HasSubscribers);
    }

    [Fact]
    public async Task Dispose_CompletesActiveSubscriptions()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        var received = new List<TestEvent>();
        var subscriberCompleted = new TaskCompletionSource();

        _ = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>())
            {
                received.Add(item);
            }
            subscriberCompleted.SetResult();
        });

        await Task.Delay(50);
        Assert.True(registry.HasSubscribers);

        await mediator.PublishAsync(new TestEvent("before-dispose"));
        await Task.Delay(50);

        // Dispose should complete all channels, causing the subscriber to exit.
        registry.Dispose();

        // Subscriber should complete within a reasonable time.
        var completed = await Task.WhenAny(subscriberCompleted.Task, Task.Delay(2000));
        Assert.Equal(subscriberCompleted.Task, completed);

        Assert.Single(received);
        Assert.Equal("before-dispose", received[0].Name);
        Assert.False(registry.HasSubscribers);

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        registry.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in mediator.SubscribeAsync<TestEvent>())
            { }
        });
    }
}
