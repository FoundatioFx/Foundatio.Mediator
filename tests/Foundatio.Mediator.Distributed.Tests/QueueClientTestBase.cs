using System.Text;
using Foundatio.Mediator.Distributed;
using Foundatio.Xunit;

namespace Foundatio.Mediator.Distributed.Tests;

/// <summary>
/// Abstract base class containing shared test cases for any <see cref="IQueueClient"/> implementation.
/// Subclasses provide the concrete client instance via <see cref="CreateClient"/>.
/// </summary>
public abstract class QueueClientTestBase(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    protected abstract IQueueClient CreateClient();

    protected virtual string TestQueueName => $"test-queue-{Guid.NewGuid():N}";

    // ── Send / Receive ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ThenReceiveAsync_ReturnsMessage()
    {
        var client = CreateClient();
        var queueName = TestQueueName;
        var body = """{"Name":"Test"}"""u8.ToArray();
        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.MessageType] = "TestMessage",
            [MessageHeaders.EnqueuedAt] = DateTimeOffset.UtcNow.ToString("O")
        };

        await client.SendAsync(queueName, new QueueEntry
        {
            Body = body,
            Headers = headers
        }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 10, TestCancellationToken);

        Assert.Single(messages);
        var msg = messages[0];
        Assert.Equal(queueName, msg.QueueName);
        Assert.Equal(body, msg.Body.ToArray());
        Assert.Equal("TestMessage", msg.Headers[MessageHeaders.MessageType]);
        Assert.False(string.IsNullOrEmpty(msg.Id));
        Assert.True(msg.DequeueCount >= 1);
    }

    [Fact]
    public async Task SendAsync_WithNoHeaders_RoundTripsBody()
    {
        var client = CreateClient();
        var queueName = TestQueueName;
        var body = """{"Value":42}"""u8.ToArray();

        await client.SendAsync(queueName, new QueueEntry { Body = body }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 10, TestCancellationToken);

        Assert.Single(messages);
        Assert.Equal(body, messages[0].Body.ToArray());
    }

    [Fact]
    public async Task ReceiveAsync_EmptyQueue_ReturnsEmptyList()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        // Use a short timeout CTS so we don't wait forever on empty queues
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var messages = await client.ReceiveAsync(queueName, 10, cts.Token);

        Assert.Empty(messages);
    }

    [Fact]
    public async Task ReceiveAsync_RespectsMaxCount()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        // Send 5 messages
        for (int i = 0; i < 5; i++)
        {
            await client.SendAsync(queueName, new QueueEntry
            {
                Body = Encoding.UTF8.GetBytes($"message-{i}")
            }, TestCancellationToken);
        }

        // Request only 2
        var messages = await client.ReceiveAsync(queueName, 2, TestCancellationToken);

        Assert.True(messages.Count is >= 1 and <= 2, $"Expected 1-2 messages but got {messages.Count}");
    }

    // ── Complete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_RemovesMessage()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "hello"u8.ToArray() }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 10, TestCancellationToken);
        Assert.Single(messages);

        await client.CompleteAsync(messages[0], TestCancellationToken);

        // Queue should now be empty — use short timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var remaining = await client.ReceiveAsync(queueName, 10, cts.Token);
        Assert.Empty(remaining);
    }

    // ── Abandon ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AbandonAsync_RequeuesMessage()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "requeue-me"u8.ToArray() }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 10, TestCancellationToken);
        Assert.Single(messages);
        var original = messages[0];

        await client.AbandonAsync(original, cancellationToken: TestCancellationToken);

        // Message should be available again
        var redelivered = await client.ReceiveAsync(queueName, 10, TestCancellationToken);
        Assert.Single(redelivered);
        Assert.Equal(original.Body.ToArray(), redelivered[0].Body.ToArray());
        Assert.True(redelivered[0].DequeueCount >= 2,
            $"Expected dequeue count >= 2 after abandon, got {redelivered[0].DequeueCount}");
    }

    // ── RenewTimeout ───────────────────────────────────────────────────────

    [Fact]
    public async Task RenewTimeoutAsync_DoesNotThrow()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "timeout-test"u8.ToArray() }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 10, TestCancellationToken);
        Assert.Single(messages);

        // Should not throw — just extends the visibility
        await client.RenewTimeoutAsync(messages[0], TimeSpan.FromMinutes(1), TestCancellationToken);

        // Clean up
        await client.CompleteAsync(messages[0], TestCancellationToken);
    }

    // ── SendBatch ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendBatchAsync_SendsAllMessages()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        var entries = Enumerable.Range(0, 3).Select(i => new QueueEntry
        {
            Body = Encoding.UTF8.GetBytes($"batch-{i}"),
            Headers = new Dictionary<string, string> { ["index"] = i.ToString() }
        }).ToList();

        await client.SendBatchAsync(queueName, entries, TestCancellationToken);

        // Receive all — may need multiple receives for SQS
        var received = new List<QueueMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (received.Count < 3 && !cts.IsCancellationRequested)
        {
            var batch = await client.ReceiveAsync(queueName, 10, cts.Token);
            received.AddRange(batch);
        }

        Assert.Equal(3, received.Count);
    }

    // ── Headers roundtrip ──────────────────────────────────────────────────

    [Fact]
    public async Task Headers_RoundTrip()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.MessageType] = "MyApp.Commands.DoWork, MyApp",
            [MessageHeaders.CorrelationId] = "corr-12345",
            [MessageHeaders.EnqueuedAt] = "2026-03-29T12:00:00Z",
            ["custom-header"] = "custom-value"
        };

        await client.SendAsync(queueName, new QueueEntry
        {
            Body = "{}"u8.ToArray(),
            Headers = headers
        }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 10, TestCancellationToken);
        Assert.Single(messages);

        foreach (var (key, value) in headers)
        {
            Assert.True(messages[0].Headers.ContainsKey(key), $"Missing header: {key}");
            Assert.Equal(value, messages[0].Headers[key]);
        }
    }

    // ── Metadata ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReceivedMessage_HasMetadata()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "meta-test"u8.ToArray() }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 10, TestCancellationToken);
        Assert.Single(messages);

        var msg = messages[0];
        Assert.False(string.IsNullOrEmpty(msg.Id));
        Assert.Equal(queueName, msg.QueueName);
        Assert.True(msg.DequeueCount >= 1);
        // EnqueuedAt and DequeuedAt should be populated
        Assert.True(msg.EnqueuedAt > DateTimeOffset.MinValue, "EnqueuedAt should be set");
        Assert.True(msg.DequeuedAt > DateTimeOffset.MinValue, "DequeuedAt should be set");
    }
}
