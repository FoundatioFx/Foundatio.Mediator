using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed.Tests;

// ── Messages ─────────────────────────────────────────────────────────

public record TrackedCommand(string Value);

public record TrackedLongRunningCommand(string Value, int Steps = 3);

public record TrackedCancellableCommand(string Value);

// ── Handlers ─────────────────────────────────────────────────────────

[Queue(TrackProgress = true)]
public class TrackedCommandHandler(HandlerSignal signal)
{
    public void Handle(TrackedCommand message)
    {
        signal.Record(message.Value);
    }
}

[Queue(TrackProgress = true)]
public class TrackedLongRunningCommandHandler(HandlerSignal signal)
{
    public async Task HandleAsync(TrackedLongRunningCommand message, QueueContext queueContext, CancellationToken ct)
    {
        for (int i = 1; i <= message.Steps; i++)
        {
            await Task.Delay(50, ct).ConfigureAwait(false);
            int percent = (int)((double)i / message.Steps * 100);
            await queueContext.ReportProgressAsync(percent, $"Step {i}/{message.Steps}", ct).ConfigureAwait(false);
        }

        signal.Record(message.Value);
    }
}

[Queue(TrackProgress = true)]
public class TrackedCancellableCommandHandler(HandlerSignal signal)
{
    public async Task HandleAsync(TrackedCancellableCommand message, QueueContext queueContext, CancellationToken ct)
    {
        // Simulate long-running work that checks cancellation via progress reporting
        for (int i = 0; i < 100; i++)
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
            await queueContext.ReportProgressAsync(i, $"Working... {i}%", ct).ConfigureAwait(false);
        }

        signal.Record(message.Value);
    }
}

// ── Tests ────────────────────────────────────────────────────────────

public class QueueWorkerJobTrackingTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    [Fact]
    public async Task TrackedHandler_EnqueueCreatesJobState()
    {
        var signal = new HandlerSignal();
        var queueClient = new InMemoryQueueClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<TrackedCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var stateStore = provider.GetRequiredService<IQueueJobStateStore>();

        // Enqueue (don't start workers) — state should be created at enqueue time
        await mediator.InvokeAsync(new TrackedCommand("test"), TestCancellationToken);

        // Read the queue message to get the jobId from headers
        var messages = await queueClient.ReceiveAsync("TrackedCommand", 10, TestCancellationToken);
        Assert.Single(messages);
        Assert.True(messages[0].Headers.ContainsKey(MessageHeaders.JobId));

        var jobId = messages[0].Headers[MessageHeaders.JobId];
        Assert.False(string.IsNullOrEmpty(jobId));

        // Verify state store has the job in Queued status
        var state = await stateStore.GetJobStateAsync(jobId, TestCancellationToken);
        Assert.NotNull(state);
        Assert.Equal(QueueJobStatus.Queued, state.Status);
        Assert.Equal("TrackedCommand", state.QueueName);
    }

    [Fact]
    public async Task TrackedHandler_CompletedJobHasCorrectState()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TrackedCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            var stateStore = provider.GetRequiredService<IQueueJobStateStore>();

            await mediator.InvokeAsync(new TrackedCommand("job-done"), cts.Token);

            // Wait for handler to execute
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(10));

            // Give the worker a moment to update state after handler completes
            await Task.Delay(200, cts.Token);

            // Find the job — there should be exactly one
            var jobs = await stateStore.GetJobsByStatusAsync("TrackedCommand", QueueJobStatus.Completed, cancellationToken: cts.Token);
            Assert.Single(jobs);

            var state = jobs[0];
            Assert.Equal(QueueJobStatus.Completed, state.Status);
            Assert.Equal(100, state.Progress);
            Assert.NotNull(state.CompletedUtc);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TrackedHandler_ProgressReporting_UpdatesState()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TrackedLongRunningCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            var stateStore = provider.GetRequiredService<IQueueJobStateStore>();

            await mediator.InvokeAsync(new TrackedLongRunningCommand("progress-test", Steps: 5), cts.Token);

            // Wait for completion
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(10));
            await Task.Delay(200, cts.Token);

            var jobs = await stateStore.GetJobsByStatusAsync("TrackedLongRunningCommand", QueueJobStatus.Completed, cancellationToken: cts.Token);
            Assert.Single(jobs);

            var state = jobs[0];
            Assert.Equal(QueueJobStatus.Completed, state.Status);
            Assert.Equal(100, state.Progress);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TrackedHandler_Cancellation_SetsStateAndCancelsToken()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TrackedCancellableCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            var stateStore = provider.GetRequiredService<IQueueJobStateStore>();

            await mediator.InvokeAsync(new TrackedCancellableCommand("cancel-test"), cts.Token);

            // Wait a bit for the handler to start processing
            await Task.Delay(500, cts.Token);

            // Find the job and request cancellation
            var jobs = await stateStore.GetJobsByStatusAsync("TrackedCancellableCommand", QueueJobStatus.Processing, cancellationToken: cts.Token);
            Assert.Single(jobs);
            var jobId = jobs[0].JobId;

            // Verify it's currently Processing
            var state = await stateStore.GetJobStateAsync(jobId, cts.Token);
            Assert.NotNull(state);
            Assert.Equal(QueueJobStatus.Processing, state.Status);

            // Request cancellation
            var cancelled = await stateStore.RequestCancellationAsync(jobId, cts.Token);
            Assert.True(cancelled);

            // Wait for cancellation to propagate (default poll interval is 5s)
            await Task.Delay(8000, cts.Token);

            // Verify the job state is now Cancelled
            state = await stateStore.GetJobStateAsync(jobId, cts.Token);
            Assert.NotNull(state);
            Assert.Equal(QueueJobStatus.Cancelled, state.Status);
            Assert.NotNull(state.CompletedUtc);

            // The handler should NOT have completed (signal should not have been recorded)
            Assert.Empty(signal.Values);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task NonTrackedHandler_StillWorksNormally()
    {
        // Existing QueuedCommand (no TrackProgress) should still work without a state store
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

            await mediator.InvokeAsync(new QueuedCommand("compat-test"), cts.Token);
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(10));

            Assert.Single(signal.Values);
            Assert.Equal("compat-test", signal.Values[0]);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WorkerRegistry_IsPopulated()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TrackedCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IQueueWorkerRegistry>();

        var workers = registry.GetWorkers();
        Assert.NotEmpty(workers);

        var worker = registry.GetWorker("TrackedCommand");
        Assert.NotNull(worker);
        Assert.Equal("TrackedCommand", worker.QueueName);
        Assert.True(worker.TrackProgress);
    }

    [Fact]
    public async Task WorkerInfo_TracksRuntimeStats()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TrackedCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            var registry = provider.GetRequiredService<IQueueWorkerRegistry>();

            await mediator.InvokeAsync(new TrackedCommand("stats-1"), cts.Token);
            await mediator.InvokeAsync(new TrackedCommand("stats-2"), cts.Token);

            await signal.WaitAsync(count: 2, timeout: TimeSpan.FromSeconds(10));
            await Task.Delay(200, cts.Token);

            var worker = registry.GetWorker("TrackedCommand");
            Assert.NotNull(worker);
            Assert.Equal(2, worker.MessagesProcessed);
            Assert.Equal(0, worker.MessagesFailed);
            Assert.True(worker.IsRunning);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task InMemoryStateStore_AutoRegistered_WhenTrackProgressEnabled()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TrackedCommandHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        // Should auto-register InMemoryQueueJobStateStore
        var stateStore = provider.GetService<IQueueJobStateStore>();
        Assert.NotNull(stateStore);
        Assert.IsType<InMemoryQueueJobStateStore>(stateStore);
    }
}
