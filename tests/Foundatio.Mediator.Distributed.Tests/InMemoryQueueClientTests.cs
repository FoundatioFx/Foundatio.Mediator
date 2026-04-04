using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Mediator.Distributed.Tests;

public class InMemoryQueueClientTests(ITestOutputHelper output) : QueueClientTestBase(output)
{
    protected override IQueueClient CreateClient() => new InMemoryQueueClient();

    [Fact]
    public async Task MultipleQueues_AreIsolated()
    {
        var client = CreateClient();
        var q1 = $"queue-a-{Guid.NewGuid():N}";
        var q2 = $"queue-b-{Guid.NewGuid():N}";

        await client.SendAsync(q1, new QueueEntry { Body = "a"u8.ToArray() }, TestCancellationToken);
        await client.SendAsync(q2, new QueueEntry { Body = "b"u8.ToArray() }, TestCancellationToken);

        var msgs1 = await client.ReceiveAsync(q1, 10, TestCancellationToken);
        var msgs2 = await client.ReceiveAsync(q2, 10, TestCancellationToken);

        Assert.Single(msgs1);
        Assert.Single(msgs2);
        Assert.Equal("a"u8.ToArray(), msgs1[0].Body.ToArray());
        Assert.Equal("b"u8.ToArray(), msgs2[0].Body.ToArray());
    }

    [Fact]
    public async Task AbandonAsync_IncrementsDequeueCount()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "retry-test"u8.ToArray() }, TestCancellationToken);

        // Receive, abandon, receive again — dequeue count should increase
        var first = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Equal(1, first[0].DequeueCount);

        await client.AbandonAsync(first[0], cancellationToken: TestCancellationToken);

        var second = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Equal(2, second[0].DequeueCount);

        await client.AbandonAsync(second[0], cancellationToken: TestCancellationToken);

        var third = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Equal(3, third[0].DequeueCount);
    }

    [Fact]
    public async Task CompleteAsync_ThenReceive_ReturnsEmpty()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "complete-test"u8.ToArray() }, TestCancellationToken);
        var msgs = await client.ReceiveAsync(queueName, 10, TestCancellationToken);
        await client.CompleteAsync(msgs[0], TestCancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var remaining = await client.ReceiveAsync(queueName, 10, cts.Token);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task SendBatchAsync_Ordering_PreservedApproximately()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        var entries = Enumerable.Range(0, 5).Select(i => new QueueEntry
        {
            Body = new byte[] { (byte)i }
        }).ToList();

        await client.SendBatchAsync(queueName, entries, TestCancellationToken);

        var received = new List<QueueMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (received.Count < 5 && !cts.IsCancellationRequested)
        {
            var batch = await client.ReceiveAsync(queueName, 10, cts.Token);
            received.AddRange(batch);
        }

        Assert.Equal(5, received.Count);
        // In-memory channel preserves FIFO order
        for (int i = 0; i < 5; i++)
            Assert.Equal((byte)i, received[i].Body.Span[0]);
    }

    [Fact]
    public async Task ConcurrentSendAndReceive_AllMessagesDelivered()
    {
        var client = CreateClient();
        var queueName = TestQueueName;
        const int messageCount = 100;

        // Send concurrently
        var sendTasks = Enumerable.Range(0, messageCount).Select(i =>
            client.SendAsync(queueName, new QueueEntry { Body = new byte[] { (byte)(i % 256) } }, TestCancellationToken));
        await Task.WhenAll(sendTasks);

        // Receive all
        var received = new List<QueueMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (received.Count < messageCount && !cts.IsCancellationRequested)
        {
            var batch = await client.ReceiveAsync(queueName, 50, cts.Token);
            received.AddRange(batch);
        }

        Assert.Equal(messageCount, received.Count);
    }

    // ── Dead-letter ────────────────────────────────────────────────────

    [Fact]
    public async Task DeadLetterAsync_MovesMessageToDLQ()
    {
        var client = (InMemoryQueueClient)CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry
        {
            Body = "poison"u8.ToArray(),
            Headers = new Dictionary<string, string> { ["custom"] = "value" }
        }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(messages);

        await client.DeadLetterAsync(messages[0], "Bad format", TestCancellationToken);

        // Original queue should be empty
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var remaining = await client.ReceiveAsync(queueName, 10, cts.Token);
        Assert.Empty(remaining);

        // DLQ should have the message
        var dlqMessages = client.DrainDeadLetterMessages(queueName);
        Assert.Single(dlqMessages);
        Assert.Equal("poison"u8.ToArray(), dlqMessages[0].Body.ToArray());
    }

    [Fact]
    public async Task DeadLetterAsync_PreservesOriginalHeaders()
    {
        var client = (InMemoryQueueClient)CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry
        {
            Body = "test"u8.ToArray(),
            Headers = new Dictionary<string, string>
            {
                [MessageHeaders.MessageType] = "MyMessage",
                ["custom-key"] = "custom-value"
            }
        }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        await client.DeadLetterAsync(messages[0], "Test reason", TestCancellationToken);

        var dlq = client.DrainDeadLetterMessages(queueName);
        Assert.Single(dlq);

        // Original headers preserved
        Assert.Equal("MyMessage", dlq[0].Headers[MessageHeaders.MessageType]);
        Assert.Equal("custom-value", dlq[0].Headers["custom-key"]);

        // Dead-letter metadata added
        Assert.Equal("Test reason", dlq[0].Headers[MessageHeaders.DeadLetterReason]);
        Assert.True(dlq[0].Headers.ContainsKey(MessageHeaders.DeadLetteredAt));
        Assert.Equal(queueName, dlq[0].Headers[MessageHeaders.OriginalQueueName]);
        Assert.True(dlq[0].Headers.ContainsKey(MessageHeaders.DeadLetterDequeueCount));
    }

    [Fact]
    public async Task DeadLetterAsync_PreservesDequeueCount()
    {
        var client = (InMemoryQueueClient)CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "dlq-count"u8.ToArray() }, TestCancellationToken);

        // Receive and abandon twice to bump dequeue count
        var msg = (await client.ReceiveAsync(queueName, 1, TestCancellationToken))[0];
        await client.AbandonAsync(msg, cancellationToken: TestCancellationToken);
        msg = (await client.ReceiveAsync(queueName, 1, TestCancellationToken))[0];
        await client.AbandonAsync(msg, cancellationToken: TestCancellationToken);
        msg = (await client.ReceiveAsync(queueName, 1, TestCancellationToken))[0];
        Assert.Equal(3, msg.DequeueCount);

        await client.DeadLetterAsync(msg, "Too many retries", TestCancellationToken);

        var dlq = client.DrainDeadLetterMessages(queueName);
        Assert.Single(dlq);
        Assert.Equal("3", dlq[0].Headers[MessageHeaders.DeadLetterDequeueCount]);
    }

    [Fact]
    public async Task GetDeadLetterCount_ReturnsCorrectCount()
    {
        var client = (InMemoryQueueClient)CreateClient();
        var queueName = TestQueueName;

        Assert.Equal(0, client.GetDeadLetterCount(queueName));

        // Dead-letter three messages
        for (int i = 0; i < 3; i++)
        {
            await client.SendAsync(queueName, new QueueEntry { Body = new byte[] { (byte)i } }, TestCancellationToken);
            var msg = (await client.ReceiveAsync(queueName, 1, TestCancellationToken))[0];
            await client.DeadLetterAsync(msg, $"reason-{i}", TestCancellationToken);
        }

        Assert.Equal(3, client.GetDeadLetterCount(queueName));
    }

    // ── Abandon with delay (FakeTimeProvider) ──────────────────────────

    [Fact]
    public async Task AbandonAsync_WithDelay_MessageNotVisibleUntilTimeAdvances()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new InMemoryQueueClient(fakeTime);
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "delayed"u8.ToArray() }, TestCancellationToken);
        var msg = (await client.ReceiveAsync(queueName, 1, TestCancellationToken))[0];

        // Start abandon with 30s delay — it will block on Task.Delay
        var abandonTask = client.AbandonAsync(msg, TimeSpan.FromSeconds(30), TestCancellationToken);

        // Message should NOT be re-enqueued yet
        Assert.False(abandonTask.IsCompleted);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var empty = await client.ReceiveAsync(queueName, 1, cts.Token);
        Assert.Empty(empty);

        // Advance time past the delay
        fakeTime.Advance(TimeSpan.FromSeconds(31));
        await abandonTask;

        // Now the message should be available
        var redelivered = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(redelivered);
        Assert.Equal("delayed"u8.ToArray(), redelivered[0].Body.ToArray());
    }

    [Fact]
    public async Task AbandonAsync_ZeroDelay_ImmediatelyRequeues()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new InMemoryQueueClient(fakeTime);
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "instant"u8.ToArray() }, TestCancellationToken);
        var msg = (await client.ReceiveAsync(queueName, 1, TestCancellationToken))[0];

        await client.AbandonAsync(msg, cancellationToken: TestCancellationToken);

        var redelivered = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(redelivered);
        Assert.Equal(2, redelivered[0].DequeueCount);
    }

    [Fact]
    public async Task Timestamps_UseFakeTimeProvider()
    {
        var fixedTime = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedTime);
        var client = new InMemoryQueueClient(fakeTime);
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "time-test"u8.ToArray() }, TestCancellationToken);

        // Advance 5 minutes before receiving
        fakeTime.Advance(TimeSpan.FromMinutes(5));

        var msg = (await client.ReceiveAsync(queueName, 1, TestCancellationToken))[0];

        Assert.Equal(fixedTime, msg.EnqueuedAt);
        Assert.Equal(fixedTime + TimeSpan.FromMinutes(5), msg.DequeuedAt);
    }
}
