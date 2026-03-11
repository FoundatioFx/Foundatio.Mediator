// These tests intentionally manage their own CancellationTokenSource to verify
// subscription cancellation and disposal behavior — TestContext.Current.CancellationToken is not appropriate here.
#pragma warning disable xUnit1051

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

    public record CascadeCommand(string Name);

    public class CascadeHandler
    {
        public (Result, TestEvent) Handle(CascadeCommand cmd)
            => (Result.Success(), new TestEvent(cmd.Name));
    }

    /// <summary>Polls until <paramref name="condition"/> returns true or the timeout expires.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
            await Task.Delay(10);
    }

    [Fact]
    public async Task SubscribeAsync_ReceivesConcrete_WhenPublished()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<TestEvent>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cts.Token))
            {
                received.Add(item);
            }
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        await mediator.PublishAsync(new TestEvent("one"));
        await mediator.PublishAsync(new TestEvent("two"));

        await WaitUntilAsync(() => received.Count >= 2);
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
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<ITestEvent>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<ITestEvent>(cts.Token))
            {
                received.Add(item);
            }
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        await mediator.PublishAsync(new TestEvent("hello"));
        await mediator.PublishAsync(new OtherEvent("world"));

        await WaitUntilAsync(() => received.Count >= 2);
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
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<TestEvent>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cts.Token))
            {
                received.Add(item);
            }
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        // Publish an unrelated type — subscriber should not receive it.
        await mediator.PublishAsync(new UnrelatedEvent("nope"));

        // Brief pause to confirm nothing arrives, then cancel.
        await Task.Delay(100);
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
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received1 = new List<TestEvent>();
        var received2 = new List<TestEvent>();

        var sub1 = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cts.Token))
                received1.Add(item);
        });

        var sub2 = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cts.Token))
                received2.Add(item);
        });

        // Wait for both subscribers to register (2 entries for TestEvent type).
        await WaitUntilAsync(() => registry.HasSubscribers);
        // Small extra delay to let the second subscriber also register.
        await Task.Delay(50);

        await mediator.PublishAsync(new TestEvent("shared"));

        await WaitUntilAsync(() => received1.Count >= 1 && received2.Count >= 1);
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
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<TestEvent>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cts.Token))
            {
                received.Add(item);
                if (received.Count == 2)
                    cts.Cancel();
            }
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        await mediator.PublishAsync(new TestEvent("a"));
        await mediator.PublishAsync(new TestEvent("b"));
        await mediator.PublishAsync(new TestEvent("c"));

        await subscriberTask;

        // Cancellation was requested after "b". The channel may still yield
        // already-buffered items ("c") before the next WaitToReadAsync detects
        // cancellation, so we assert 2..3 items were received.
        Assert.InRange(received.Count, 2, 3);
        Assert.Equal("a", received[0].Name);
        Assert.Equal("b", received[1].Name);
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
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cts.Token, new SubscriberOptions { MaxCapacity = 2 }))
            {
                received.Add(item);
            }
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        // Publish 5 items rapidly — buffer should drop oldest and keep newest.
        for (int i = 0; i < 5; i++)
            await mediator.PublishAsync(new TestEvent($"msg-{i}"));

        // Wait for at least one item, then give extra time for draining.
        await WaitUntilAsync(() => received.Count > 0);
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
            await foreach (var _ in mediator.SubscribeAsync<TestEvent>(cts.Token))
            { }
        });

        await WaitUntilAsync(() => registry.HasSubscribers);
        Assert.True(registry.HasSubscribers);

        cts.Cancel();
        await subscriberTask;

        // After cancellation removes the subscription.
        await WaitUntilAsync(() => !registry.HasSubscribers);
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

        await WaitUntilAsync(() => registry.HasSubscribers);

        await mediator.PublishAsync(new TestEvent("before-dispose"));
        await WaitUntilAsync(() => received.Count >= 1);

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

    [Fact]
    public async Task SubscribeAsync_ReceivesCascadingEvents()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddMediator(b => b.AddAssembly<TestEvent>());

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<TestEvent>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<TestEvent>(cts.Token))
            {
                received.Add(item);
            }
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        // Invoke a command whose handler returns a cascading TestEvent via tuple
        await mediator.InvokeAsync<Result>(new CascadeCommand("from-cascade"));

        await WaitUntilAsync(() => received.Count >= 1);
        cts.Cancel();
        await subscriberTask;

        Assert.Single(received);
        Assert.Equal("from-cascade", received[0].Name);
    }
}
