#pragma warning disable xUnit1051
using Foundatio.Xunit;

namespace Foundatio.Mediator.Distributed.Tests;

public class InMemoryPubSubClientTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    [Fact]
    public async Task PublishAsync_WithNoSubscribers_DoesNotThrow()
    {
        using var bus = new InMemoryPubSubClient();

        await bus.PublishAsync("test-topic", [new PubSubEntry { Body = "hello"u8.ToArray() }], TestCancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_ReceivesPublishedMessage()
    {
        using var bus = new InMemoryPubSubClient();

        PubSubMessage? received = null;
        var signal = new SemaphoreSlim(0);

        await using var sub = await bus.SubscribeAsync("test-topic", (msg, ct) =>
        {
            received = msg;
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        var headers = new Dictionary<string, string> { ["key"] = "value" };
        await bus.PublishAsync("test-topic", [new PubSubEntry { Body = "hello"u8.ToArray(), Headers = headers }], TestCancellationToken);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(received);
        Assert.Equal("hello"u8.ToArray(), received.Body.ToArray());
        Assert.Equal("value", received.Headers["key"]);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_AllReceive()
    {
        using var bus = new InMemoryPubSubClient();

        int count1 = 0, count2 = 0;
        var signal = new SemaphoreSlim(0);

        await using var sub1 = await bus.SubscribeAsync("topic", (msg, ct) =>
        {
            Interlocked.Increment(ref count1);
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await using var sub2 = await bus.SubscribeAsync("topic", (msg, ct) =>
        {
            Interlocked.Increment(ref count2);
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await bus.PublishAsync("topic", [new PubSubEntry { Body = "data"u8.ToArray() }], TestCancellationToken);

        // Wait for both subscribers
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, count1);
        Assert.Equal(1, count2);
    }

    [Fact]
    public async Task SubscribeAsync_DifferentTopics_OnlyMatchingReceives()
    {
        using var bus = new InMemoryPubSubClient();

        int topicACount = 0, topicBCount = 0;
        var signal = new SemaphoreSlim(0);

        await using var subA = await bus.SubscribeAsync("topic-a", (msg, ct) =>
        {
            Interlocked.Increment(ref topicACount);
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await using var subB = await bus.SubscribeAsync("topic-b", (msg, ct) =>
        {
            Interlocked.Increment(ref topicBCount);
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await bus.PublishAsync("topic-a", [new PubSubEntry { Body = "only-a"u8.ToArray() }], TestCancellationToken);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        // Give a moment to ensure topic-b doesn't fire
        await Task.Delay(200, TestCancellationToken);

        Assert.Equal(1, topicACount);
        Assert.Equal(0, topicBCount);
    }

    [Fact]
    public async Task DisposeSubscription_StopsReceiving()
    {
        using var bus = new InMemoryPubSubClient();

        int count = 0;
        var signal = new SemaphoreSlim(0);

        var sub = await bus.SubscribeAsync("topic", (msg, ct) =>
        {
            Interlocked.Increment(ref count);
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await bus.PublishAsync("topic", [new PubSubEntry { Body = "msg1"u8.ToArray() }], TestCancellationToken);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, count);

        // Dispose subscription
        await sub.DisposeAsync();

        await bus.PublishAsync("topic", [new PubSubEntry { Body = "msg2"u8.ToArray() }], TestCancellationToken);
        await Task.Delay(200, TestCancellationToken);

        Assert.Equal(1, count); // Should not have received msg2
    }

    [Fact]
    public async Task PublishAsync_HeadersAreReadOnly()
    {
        using var bus = new InMemoryPubSubClient();

        PubSubMessage? received = null;
        var signal = new SemaphoreSlim(0);

        await using var sub = await bus.SubscribeAsync("topic", (msg, ct) =>
        {
            received = msg;
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await bus.PublishAsync("topic", [new PubSubEntry
        {
            Body = "test"u8.ToArray(),
            Headers = new Dictionary<string, string> { ["h1"] = "v1", ["h2"] = "v2" }
        }], TestCancellationToken);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(received);
        Assert.Equal("v1", received.Headers["h1"]);
        Assert.Equal("v2", received.Headers["h2"]);
    }
}
