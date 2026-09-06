using Foundatio.Mediator.Distributed;
using Xunit;

namespace Foundatio.Mediator.Distributed.Tests;

public abstract class QueueJobStateStoreContractTests
{
    protected abstract IQueueJobStateStore CreateStore();
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OlderAttempt_CannotOverwriteNewerOrTerminalState()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(new QueueJobState { JobId = "job", QueueName = "work" }, cancellationToken: CT);
        Assert.True(await store.UpdateJobStatusAsync("job", QueueJobStatus.Processing, attempt: 1, cancellationToken: CT));
        Assert.True(await store.UpdateJobStatusAsync("job", QueueJobStatus.RetryPending, attempt: 1, cancellationToken: CT));
        Assert.False(await store.UpdateJobStatusAsync("job", QueueJobStatus.Processing, attempt: 1, cancellationToken: CT));
        Assert.True(await store.UpdateJobStatusAsync("job", QueueJobStatus.Processing, attempt: 2, cancellationToken: CT));
        await store.UpdateJobProgressAsync("job", 40, cancellationToken: CT, expectedAttempt: 2);
        await store.UpdateJobProgressAsync("job", 90, cancellationToken: CT, expectedAttempt: 1);
        Assert.False(await store.UpdateJobStatusAsync("job", QueueJobStatus.Failed, attempt: 1, cancellationToken: CT));
        Assert.Equal(40, (await store.GetJobStateAsync("job", CT))!.Progress);
        Assert.True(await store.UpdateJobStatusAsync("job", QueueJobStatus.Completed, attempt: 2, progress: 100, cancellationToken: CT));
        var terminal = await store.GetJobStateAsync("job", CT);
        await store.UpdateJobProgressAsync("job", 10, cancellationToken: CT, expectedAttempt: 2);
        await store.HeartbeatAsync("job", CT, expectedAttempt: 2);
        Assert.False(await store.UpdateJobStatusAsync("job", QueueJobStatus.Processing, attempt: 3, cancellationToken: CT));
        Assert.Equal(terminal, await store.GetJobStateAsync("job", CT));
    }

    [Fact]
    public async Task ConcurrentAttempts_PreserveNewestAttemptAndStatusIndex()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(new QueueJobState { JobId = "job", QueueName = "work" }, cancellationToken: CT);
        await Task.WhenAll(Enumerable.Range(1, 100).Select(i => Task.Run(() => store.UpdateJobStatusAsync("job",
            i % 2 == 0 ? QueueJobStatus.RetryPending : QueueJobStatus.Processing, attempt: i, cancellationToken: CT), CT)));
        var state = await store.GetJobStateAsync("job", CT);
        Assert.NotNull(state);
        Assert.Equal(100, state.Attempt);
        Assert.Equal(QueueJobStatus.RetryPending, state.Status);
        Assert.Equal(1, await store.GetJobCountByStatusAsync("work", state.Status, CT));
        Assert.Equal(0, await store.GetJobCountByStatusAsync("work", QueueJobStatus.Processing, CT));
        Assert.True(await store.RequestCancellationAsync("job", CT));
    }

    [Fact]
    public async Task ExplicitReset_ClearsCancellationAndTerminalState()
    {
        var store = CreateStore();
        var initial = new QueueJobState { JobId = "job", QueueName = "work" };
        await store.SetJobStateAsync(initial, cancellationToken: CT);
        Assert.True(await store.RequestCancellationAsync("job", CT));
        Assert.True(await store.UpdateJobStatusAsync("job", QueueJobStatus.Cancelled, cancellationToken: CT));
        await store.SetJobStateAsync(initial, cancellationToken: CT);
        Assert.False(await store.IsCancellationRequestedAsync("job", CT));
        Assert.True(await store.UpdateJobStatusAsync("job", QueueJobStatus.Processing, attempt: 1, cancellationToken: CT));
    }
}
