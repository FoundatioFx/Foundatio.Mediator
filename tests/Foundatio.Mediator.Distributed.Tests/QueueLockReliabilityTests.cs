using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Mediator.Distributed.Tests;

public class QueueLockReliabilityTests
{
    [Fact]
    public async Task LateAcquisition_AfterCancellation_IsReleased()
    {
        var locks = new TestLockProvider { DelayAcquisition = true };
        await using var provider = CreateProvider(locks);
        var topology = provider.GetRequiredService<QueueTopology>();
        var registration = Assert.Single(topology.GetByQueueName("LockLifetimeCommand")!.Handlers);
        var middleware = new QueueLockMiddleware(topology, NullLogger<QueueLockMiddleware>.Instance,
            provider.GetRequiredService<IQueueClient>(), locks);
        using var context = CallContext.Rent().Set(new QueueContext { QueueName = "LockLifetimeCommand" });
        using var cancellation = new CancellationTokenSource();
        var invocation = middleware.ExecuteAsync(new LockLifetimeCommand(), () => throw new InvalidOperationException("Handler must not execute"),
            new HandlerExecutionInfo(typeof(LockLifetimeCommandHandler), typeof(LockLifetimeCommandHandler).GetMethod("HandleAsync")!, registration.DescriptorId),
            context, cancellation.Token).AsTask();
        await locks.Acquiring.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
        locks.Acquired.TrySetResult(locks.Lease);
        await locks.Lease.Released.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LostLock_CancelsCooperativeHandler_AndReleasesLease()
    {
        var time = new FakeTimeProvider();
        var locks = new TestLockProvider();
        await using var provider = CreateProvider(locks, time);
        var signal = provider.GetRequiredService<LockLifetimeSignal>();
        var hosted = provider.GetServices<IHostedService>().ToArray();
        foreach (var service in hosted) await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await provider.GetRequiredService<IMediator>().InvokeAsync(new LockLifetimeCommand(), TestContext.Current.CancellationToken);
            await signal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromSeconds(6));
            await signal.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await locks.Lease.Released.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            foreach (var service in hosted) await service.StopAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider CreateProvider(TestLockProvider locks, TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time ?? TimeProvider.System);
        services.AddSingleton<IQueueClient>(new InMemoryQueueClient(time));
        services.AddSingleton<IQueueLockProvider>(locks);
        services.AddSingleton<LockLifetimeSignal>();
        services.AddMediator(b => b.AddAssembly<LockLifetimeCommandHandler>())
            .AddDistributedQueues(o => o.Workers = WorkerSelection.Only("LockLifetimeCommand"));
        return services.BuildServiceProvider();
    }

    private sealed class TestLockProvider : IQueueLockProvider
    {
        public bool DelayAcquisition { get; init; }
        public TestLock Lease { get; } = new();
        public TaskCompletionSource Acquiring { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IQueueLock?> Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IQueueLock?> TryAcquireAsync(string key, TimeSpan lifetime, TimeSpan acquireTimeout, CancellationToken cancellationToken = default)
        {
            Acquiring.TrySetResult();
            return DelayAcquisition ? Acquired.Task : Task.FromResult<IQueueLock?>(Lease);
        }
    }

    private sealed class TestLock : IQueueLock
    {
        public string Key => "resource";
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RenewAsync(TimeSpan lifetime, CancellationToken cancellationToken = default) => throw new QueueLeaseLostException("Lock ownership lost");
        public ValueTask DisposeAsync() { Released.TrySetResult(); return ValueTask.CompletedTask; }
    }
}

public record LockLifetimeCommand;

public class LockLifetimeSignal
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

[Queue]
[QueueLock(LifetimeSeconds = 10)]
public class LockLifetimeCommandHandler
{
    public async Task HandleAsync(LockLifetimeCommand message, LockLifetimeSignal signal, CancellationToken cancellationToken)
    {
        signal.Entered.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException) { signal.Cancelled.TrySetResult(); throw; }
    }
}
