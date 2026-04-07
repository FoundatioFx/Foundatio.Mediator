using System.Text.Json;
using Foundatio.Mediator.Distributed;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed.Tests;

// ── Messages ─────────────────────────────────────────────────────────
public record QueuedCommand(string Value);
public record QueuedQuery(string Value);

// ── Thread-safe signal for async handler completion ──────────────────
public class HandlerSignal
{
    private readonly SemaphoreSlim _semaphore = new(0);
    private readonly List<string> _values = [];

    public IReadOnlyList<string> Values
    {
        get { lock (_values) return [.. _values]; }
    }

    public void Record(string value)
    {
        lock (_values) _values.Add(value);
        _semaphore.Release();
    }

    public async Task WaitAsync(int count = 1, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(10);
        for (int i = 0; i < count; i++)
        {
            if (!await _semaphore.WaitAsync(timeout.Value))
                throw new TimeoutException($"Timed out waiting for handler signal (expected {count}, got {i})");
        }
    }
}

// ── Queue handlers (DI-injected signal for test isolation) ───────────

[Queue]
public class QueuedCommandHandler(HandlerSignal signal)
{
    public void Handle(QueuedCommand message)
    {
        signal.Record(message.Value);
    }
}

[Queue]
public class QueuedQueryHandler(HandlerSignal signal)
{
    public string Handle(QueuedQuery message)
    {
        signal.Record(message.Value);
        return $"Processed: {message.Value}";
    }
}

// ── Tests ────────────────────────────────────────────────────────────

public class QueueWorkerIntegrationTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    [Fact]
    public async Task QueuedHandler_InvokeAsync_EnqueuesAndProcesses()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<QueuedCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        // Start the hosted services (QueueWorker)
        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();

            // InvokeAsync should enqueue (not execute handler inline)
            await mediator.InvokeAsync(new QueuedCommand("hello"), cts.Token);

            // Wait for the worker to process the message
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(10));

            Assert.Single(signal.Values);
            Assert.Equal("hello", signal.Values[0]);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task QueuedHandler_MultipleMessages_AllProcessed()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<QueuedCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();

            // Send multiple messages
            for (int i = 0; i < 5; i++)
                await mediator.InvokeAsync(new QueuedCommand($"msg-{i}"), cts.Token);

            // Wait for all to be processed
            await signal.WaitAsync(count: 5, timeout: TimeSpan.FromSeconds(10));

            Assert.Equal(5, signal.Values.Count);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task QueueMiddleware_EnqueuePath_SerializesCorrectly()
    {
        // Verify the enqueue path serializes the message correctly by directly reading from the queue
        var signal = new HandlerSignal();
        var queueClient = new InMemoryQueueClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<QueuedCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Do NOT start hosted services — just enqueue
        await mediator.InvokeAsync(new QueuedCommand("serialize-test"), TestCancellationToken);

        // Read directly from the queue client
        var messages = await queueClient.ReceiveAsync("QueuedCommand", 10, TestCancellationToken);
        Assert.Single(messages);

        // Verify headers
        Assert.True(messages[0].Headers.ContainsKey(MessageHeaders.MessageType));
        Assert.Contains("QueuedCommand", messages[0].Headers[MessageHeaders.MessageType]);
        Assert.True(messages[0].Headers.ContainsKey(MessageHeaders.EnqueuedAt));

        // Verify body deserializes back
        var deserialized = JsonSerializer.Deserialize<QueuedCommand>(messages[0].Body.Span);
        Assert.NotNull(deserialized);
        Assert.Equal("serialize-test", deserialized.Value);
    }

    [Fact]
    public async Task QueueWorker_InjectsQueueContext()
    {
        // Tests that QueueContext is available to handlers during processing
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<QueueContextCheckHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.InvokeAsync(new QueueContextCheck("ctx-test"), cts.Token);

            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(10));

            Assert.True(QueueContextCheckHandler.HadQueueContext, "Handler should have received QueueContext");
            Assert.Equal("QueueContextCheck", QueueContextCheckHandler.ReceivedQueueName);
            Assert.True(QueueContextCheckHandler.ReceivedDequeueCount >= 1);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }
}

// ── Handler that checks for QueueContext injection ───────────────────

public record QueueContextCheck(string Value);

[Queue]
public class QueueContextCheckHandler(HandlerSignal signal)
{
    public static bool HadQueueContext { get; set; }
    public static string? ReceivedQueueName { get; set; }
    public static int ReceivedDequeueCount { get; set; }

    public void Handle(QueueContextCheck message, QueueContext queueContext)
    {
        HadQueueContext = true;
        ReceivedQueueName = queueContext.QueueName;
        ReceivedDequeueCount = queueContext.DequeueCount;
        signal.Record(message.Value);
    }
}

// ── Handler that always throws (for retry/dead-letter testing) ───────

public record PoisonMessage(string Value);

[Queue(MaxAttempts = 3, RetryPolicy = QueueRetryPolicy.None)]
public class PoisonMessageHandler(HandlerSignal signal)
{
    public void Handle(PoisonMessage message)
    {
        signal.Record($"attempt-{message.Value}");
        throw new InvalidOperationException("Simulated failure");
    }
}

// ── Handler that fails N times then succeeds (transient failure) ─────

public record TransientMessage(string Value);

/// <summary>
/// Tracks how many times Handle has been called per message value.
/// Throws on the first call, succeeds on subsequent calls.
/// </summary>
public class TransientFailureTracker
{
    private int _callCount;
    public int FailCount { get; set; } = 1;
    public int CallCount => _callCount;
    public int Increment() => Interlocked.Increment(ref _callCount);
}

[Queue(MaxAttempts = 3, RetryPolicy = QueueRetryPolicy.None)]
public class TransientMessageHandler(HandlerSignal signal, TransientFailureTracker tracker)
{
    public void Handle(TransientMessage message)
    {
        var attempt = tracker.Increment();
        signal.Record($"attempt-{attempt}");

        if (attempt <= tracker.FailCount)
            throw new InvalidOperationException($"Transient failure (attempt {attempt})");
    }
}

// ── Handler for MaxAttempts=1 (no retries, immediate dead-letter) ─────

public record NoRetryMessage(string Value);

[Queue(MaxAttempts = 1, RetryPolicy = QueueRetryPolicy.None)]
public class NoRetryMessageHandler(HandlerSignal signal)
{
    public void Handle(NoRetryMessage message)
    {
        signal.Record($"attempt-{message.Value}");
        throw new InvalidOperationException("Always fails");
    }
}

// ── Dead-letter integration tests ────────────────────────────────────

public class QueueWorkerDeadLetterTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    [Fact]
    public async Task FailedMessage_IsDeadLettered_AfterMaxAttempts()
    {
        var signal = new HandlerSignal();
        var queueClient = new InMemoryQueueClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<PoisonMessageHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.InvokeAsync(new PoisonMessage("test"), cts.Token);

            // Handler throws every time. MaxAttempts=3 means 3 attempts
            // then dead-lettered on the 4th receive.
            // Wait for the handler to be called (up to 3 times) + dead-letter
            await signal.WaitAsync(count: 3, timeout: TimeSpan.FromSeconds(10));

            // Give the worker a moment to dead-letter after the 3rd failure
            await Task.Delay(500, cts.Token);

            Assert.Equal(3, signal.Values.Count);

            // Verify message ended up in DLQ
            var dlqCount = queueClient.GetDeadLetterCount("PoisonMessage");
            Assert.Equal(1, dlqCount);

            var dlqMessages = queueClient.DrainDeadLetterMessages("PoisonMessage");
            Assert.Single(dlqMessages);
            Assert.Contains("max attempts", dlqMessages[0].Headers[MessageHeaders.DeadLetterReason], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TransientFailure_SucceedsOnRetry_NotDeadLettered()
    {
        var signal = new HandlerSignal();
        var tracker = new TransientFailureTracker { FailCount = 1 };
        var queueClient = new InMemoryQueueClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddSingleton(tracker);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<TransientMessageHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.InvokeAsync(new TransientMessage("transient"), cts.Token);

            // First attempt fails, second attempt succeeds
            await signal.WaitAsync(count: 2, timeout: TimeSpan.FromSeconds(10));

            // Give the worker a moment to process
            await Task.Delay(500, cts.Token);

            Assert.Equal(2, tracker.CallCount);
            Assert.Equal("attempt-1", signal.Values[0]);
            Assert.Equal("attempt-2", signal.Values[1]);

            // Should NOT be dead-lettered
            Assert.Equal(0, queueClient.GetDeadLetterCount("TransientMessage"));
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MaxAttemptsOne_DeadLettersOnFirstFailure()
    {
        var signal = new HandlerSignal();
        var queueClient = new InMemoryQueueClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<NoRetryMessageHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.InvokeAsync(new NoRetryMessage("no-retry"), cts.Token);

            // MaxAttempts=1 means 1 attempt only; handler is called once, then dead-lettered on 2nd receive
            await signal.WaitAsync(count: 1, timeout: TimeSpan.FromSeconds(10));

            // Give the worker time to dead-letter
            await Task.Delay(500, cts.Token);

            Assert.Single(signal.Values);

            var dlqCount = queueClient.GetDeadLetterCount("NoRetryMessage");
            Assert.Equal(1, dlqCount);

            var dlqMessages = queueClient.DrainDeadLetterMessages("NoRetryMessage");
            Assert.Single(dlqMessages);
            Assert.Contains("max attempts", dlqMessages[0].Headers[MessageHeaders.DeadLetterReason], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }
}
