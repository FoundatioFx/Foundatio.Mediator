using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Mediator.Distributed.Tests;

// ── Messages and handlers ────────────────────────────────────────────

public record RenewedCommand(string Value);
public record DrainableCommand(string Value, int DelayMs);

/// <summary>
/// Lets a test hold a handler open until it decides to release it.
/// </summary>
public sealed class HandlerGate
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;
    public bool WasCancelled { get; private set; }

    public void MarkStarted() => _started.TrySetResult();

    public void Release() => _release.TrySetResult();

    public async Task HoldAsync(CancellationToken ct)
    {
        _started.TrySetResult();
        try
        {
            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            WasCancelled = true;
            throw;
        }
    }
}

[Queue(TimeoutSeconds = 30)]
public class RenewedCommandHandler(HandlerGate gate, HandlerSignal signal)
{
    public async Task HandleAsync(RenewedCommand message, CancellationToken ct)
    {
        await gate.HoldAsync(ct);
        signal.Record(message.Value);
    }
}

[Queue]
public class DrainableCommandHandler(HandlerGate gate, HandlerSignal signal)
{
    public async Task HandleAsync(DrainableCommand message, CancellationToken ct)
    {
        if (message.DelayMs < 0)
        {
            await gate.HoldAsync(ct);
        }
        else
        {
            gate.MarkStarted();
            await Task.Delay(message.DelayMs, ct);
        }

        signal.Record(message.Value);
    }
}

/// <summary>
/// Single-slot queue: returns the enqueued message once, then blocks receives until cancelled.
/// Renewal throws on the first call so the worker's retry-on-next-tick behaviour can be observed.
/// </summary>
internal sealed class FlakyRenewQueueClient : IQueueClient
{
    private readonly Queue<QueueMessage> _pending = new();
    private int _renewCalls;
    private int _completes;

    public int RenewCalls => _renewCalls;
    public int Completes => _completes;

    public Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
    {
        lock (_pending)
        {
            foreach (var entry in entries)
            {
                _pending.Enqueue(new QueueMessage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Body = entry.Body,
                    Headers = entry.Headers ?? new Dictionary<string, string>(),
                    QueueName = queueName,
                    DequeueCount = 1,
                    EnqueuedAt = DateTimeOffset.UtcNow,
                    DequeuedAt = DateTimeOffset.UtcNow
                });
            }
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            lock (_pending)
            {
                if (_pending.Count > 0)
                    return [_pending.Dequeue()];
            }

            try { await Task.Delay(20, cancellationToken); }
            catch (OperationCanceledException) { return []; }
        }
    }

    public Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _completes);
        return Task.CompletedTask;
    }

    public Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _renewCalls) == 1)
            throw new InvalidOperationException("Simulated throttling");
        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// ── Tests ────────────────────────────────────────────────────────────

public class QueueWorkerLifecycleTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    [Fact]
    public async Task AutoRenew_ContinuesAfterATransientFailure()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new HandlerGate();
        var signal = new HandlerSignal();
        var client = new FlakyRenewQueueClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(gate);
        services.AddSingleton(signal);
        services.AddSingleton<TimeProvider>(fakeTime);
        services.AddSingleton<IQueueClient>(client);
        services.AddMediator(b => b.AddAssembly<RenewedCommandHandler>())
            .AddDistributedQueues(o => o.Workers = WorkerSelection.Only("RenewedCommand"));

        await using var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var svc in hosted)
            await svc.StartAsync(cts.Token);

        try
        {
            await provider.GetRequiredService<IMediator>().InvokeAsync(new RenewedCommand("renew"), cts.Token);
            await gate.Started.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);

            // The first renewal fails; retry must happen before the original lease expires.
            fakeTime.Advance(TimeSpan.FromSeconds(16));
            await WaitUntilAsync(() => client.RenewCalls >= 1, cts.Token);
            fakeTime.Advance(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => client.RenewCalls >= 2, cts.Token);

            gate.Release();
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => client.Completes == 1, cts.Token);

            Assert.False(gate.WasCancelled);
        }
        finally
        {
            foreach (var svc in hosted)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Stop_LetsAShortHandlerFinishInsideTheDrainWindow()
    {
        var gate = new HandlerGate();
        var signal = new HandlerSignal();
        var queueClient = new InMemoryQueueClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(gate);
        services.AddSingleton(signal);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<DrainableCommandHandler>())
            .AddDistributedQueues(o =>
            {
                o.Workers = WorkerSelection.Only("DrainableCommand");
                o.ShutdownTimeout = TimeSpan.FromSeconds(5);
            });

        await using var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var svc in hosted)
            await svc.StartAsync(cts.Token);

        await provider.GetRequiredService<IMediator>().InvokeAsync(new DrainableCommand("short", 700), cts.Token);
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);

        foreach (var svc in hosted)
            await svc.StopAsync(CancellationToken.None);

        Assert.Single(signal.Values);
        Assert.Equal("short", signal.Values[0]);
        Assert.Equal(0, queueClient.GetPendingCount("DrainableCommand"));
        Assert.Equal(0, queueClient.GetInFlightCount("DrainableCommand"));
        Assert.Equal(0, queueClient.GetDeadLetterCount("DrainableCommand"));
    }

    [Fact]
    public async Task Stop_CancelsAndAbandonsAHandlerThatOutlivesTheDrainWindow()
    {
        var gate = new HandlerGate();
        var signal = new HandlerSignal();
        var queueClient = new InMemoryQueueClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(gate);
        services.AddSingleton(signal);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<DrainableCommandHandler>())
            .AddDistributedQueues(o =>
            {
                o.Workers = WorkerSelection.Only("DrainableCommand");
                o.ShutdownTimeout = TimeSpan.FromMilliseconds(300);
            });

        await using var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var svc in hosted)
            await svc.StartAsync(cts.Token);

        await provider.GetRequiredService<IMediator>().InvokeAsync(new DrainableCommand("long", -1), cts.Token);
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);

        foreach (var svc in hosted)
            await svc.StopAsync(CancellationToken.None);

        Assert.True(gate.WasCancelled);
        Assert.Empty(signal.Values);
        Assert.Equal(1, queueClient.GetPendingCount("DrainableCommand"));
        Assert.Equal(0, queueClient.GetInFlightCount("DrainableCommand"));
        Assert.Equal(0, queueClient.GetDeadLetterCount("DrainableCommand"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time");
            await Task.Delay(20, ct);
        }
    }
}
