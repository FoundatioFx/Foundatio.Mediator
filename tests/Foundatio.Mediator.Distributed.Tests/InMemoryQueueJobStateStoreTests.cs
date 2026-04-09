using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Mediator.Distributed.Tests;

public class InMemoryQueueJobStateStoreTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    private InMemoryQueueJobStateStore CreateStore() => new(_time);

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private QueueJobState CreateJobState(string jobId = "job-1", string queueName = "TestQueue", QueueJobStatus status = QueueJobStatus.Queued)
    {
        return new QueueJobState
        {
            JobId = jobId,
            QueueName = queueName,
            MessageType = "TestMessage",
            Status = status,
            CreatedUtc = _time.GetUtcNow(),
            LastUpdatedUtc = _time.GetUtcNow()
        };
    }

    [Fact]
    public async Task SetAndGet_RoundTrips()
    {
        var store = CreateStore();
        var state = CreateJobState();

        await store.SetJobStateAsync(state, cancellationToken: CT);

        var retrieved = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(retrieved);
        Assert.Equal("job-1", retrieved.JobId);
        Assert.Equal("TestQueue", retrieved.QueueName);
        Assert.Equal(QueueJobStatus.Queued, retrieved.Status);
    }

    [Fact]
    public async Task GetJobState_NonExistent_ReturnsNull()
    {
        var store = CreateStore();
        var result = await store.GetJobStateAsync("nonexistent", CT);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetJobState_UpdatesExisting()
    {
        var store = CreateStore();
        var state = CreateJobState();
        await store.SetJobStateAsync(state, cancellationToken: CT);

        var updated = state with { Status = QueueJobStatus.Processing, Progress = 50 };
        await store.SetJobStateAsync(updated, cancellationToken: CT);

        var retrieved = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(retrieved);
        Assert.Equal(QueueJobStatus.Processing, retrieved.Status);
        Assert.Equal(50, retrieved.Progress);
    }

    [Fact]
    public async Task GetJobsByStatus_ReturnsMatchingJobs()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState("job-1", "QueueA"), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "QueueA"), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-3", "QueueB"), cancellationToken: CT);

        var jobsA = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Equal(2, jobsA.Count);

        var jobsB = await store.GetJobsByStatusAsync("QueueB", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Single(jobsB);
    }

    [Fact]
    public async Task GetJobsByStatus_OrdersByCreatedUtcDescending()
    {
        var store = CreateStore();
        var state1 = CreateJobState("job-1", "QueueA");
        await store.SetJobStateAsync(state1, cancellationToken: CT);

        _time.Advance(TimeSpan.FromMinutes(1));
        var state2 = CreateJobState("job-2", "QueueA");
        await store.SetJobStateAsync(state2, cancellationToken: CT);

        var jobs = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Equal(2, jobs.Count);
        Assert.Equal("job-2", jobs[0].JobId); // newer first
        Assert.Equal("job-1", jobs[1].JobId);
    }

    [Fact]
    public async Task GetJobsByStatus_SupportsPagination()
    {
        var store = CreateStore();
        for (int i = 1; i <= 5; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(i));
            await store.SetJobStateAsync(CreateJobState($"job-{i}", "QueueA"), cancellationToken: CT);
        }

        var page1 = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, skip: 0, take: 2, cancellationToken: CT);
        Assert.Equal(2, page1.Count);

        var page2 = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, skip: 2, take: 2, cancellationToken: CT);
        Assert.Equal(2, page2.Count);

        var page3 = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, skip: 4, take: 2, cancellationToken: CT);
        Assert.Single(page3);
    }

    [Fact]
    public async Task RequestCancellation_SetsFlag()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), cancellationToken: CT);

        var result = await store.RequestCancellationAsync("job-1", CT);
        Assert.True(result);

        var isCancelled = await store.IsCancellationRequestedAsync("job-1", CT);
        Assert.True(isCancelled);
    }

    [Fact]
    public async Task RequestCancellation_NonExistent_ReturnsFalse()
    {
        var store = CreateStore();
        var result = await store.RequestCancellationAsync("nonexistent", CT);
        Assert.False(result);
    }

    [Fact]
    public async Task RequestCancellation_TerminalState_ReturnsFalse()
    {
        var store = CreateStore();
        var state = CreateJobState(status: QueueJobStatus.Completed);
        await store.SetJobStateAsync(state, cancellationToken: CT);

        var result = await store.RequestCancellationAsync("job-1", CT);
        Assert.False(result);
    }

    [Fact]
    public async Task IsCancellationRequested_NotRequested_ReturnsFalse()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), cancellationToken: CT);

        var isCancelled = await store.IsCancellationRequestedAsync("job-1", CT);
        Assert.False(isCancelled);
    }

    [Fact]
    public async Task RemoveJobState_RemovesEntry()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), cancellationToken: CT);

        await store.RemoveJobStateAsync("job-1", CT);

        var result = await store.GetJobStateAsync("job-1", CT);
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveJobState_ClearsCancellation()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), cancellationToken: CT);
        await store.RequestCancellationAsync("job-1", CT);

        await store.RemoveJobStateAsync("job-1", CT);

        var isCancelled = await store.IsCancellationRequestedAsync("job-1", CT);
        Assert.False(isCancelled);
    }

    [Fact]
    public async Task ExpiredState_ReturnsNull()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), expiry: TimeSpan.FromMinutes(5), cancellationToken: CT);

        // Advance past expiry
        _time.Advance(TimeSpan.FromMinutes(6));

        var result = await store.GetJobStateAsync("job-1", CT);
        Assert.Null(result);
    }

    [Fact]
    public async Task NonExpiredState_StillReturned()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), expiry: TimeSpan.FromMinutes(5), cancellationToken: CT);

        _time.Advance(TimeSpan.FromMinutes(3));

        var result = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExpiredJobs_ExcludedFromStatusListing()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState("job-1", "QueueA"), expiry: TimeSpan.FromMinutes(1), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "QueueA"), expiry: TimeSpan.FromMinutes(10), cancellationToken: CT);

        _time.Advance(TimeSpan.FromMinutes(2));

        var jobs = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Single(jobs);
        Assert.Equal("job-2", jobs[0].JobId);
    }
}
