using Common.Module.Messages;
using Common.Module.Middleware;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;

namespace Common.Module.Handlers;

/// <summary>
/// Queue dashboard handler — exposes queue workers, job tracking, and cancellation
/// as mediator endpoints under <c>/api/queues</c>.
///
/// Uses <c>[Cached]</c> on read-heavy endpoints so multiple browser tabs or
/// overlapping poll intervals share a single SQS/Redis call instead of each
/// hitting the transport independently.
/// </summary>
[HandlerEndpointGroup("Queues")]
[HandlerAllowAnonymous]
public class QueueDashboardHandler
{
    private readonly IQueueWorkerRegistry _registry;
    private readonly IQueueClient _queueClient;
    private readonly IQueueJobStateStore? _stateStore;
    private readonly DistributedInfrastructureReady? _infraReady;

    public QueueDashboardHandler(
        IQueueWorkerRegistry registry,
        IQueueClient queueClient,
        IQueueJobStateStore? stateStore = null,
        DistributedInfrastructureReady? infraReady = null)
    {
        _registry = registry;
        _queueClient = queueClient;
        _stateStore = stateStore;
        _infraReady = infraReady;
    }

    [Cached(DurationSeconds = 2)]
    public async Task<Result<List<QueueSummary>>> HandleAsync(GetQueues query, CancellationToken ct)
    {
        var workers = _registry.GetWorkers();

        // Fetch all queue stats in parallel — each is an independent SQS call.
        var tasks = new Task<QueueSummary>[workers.Count];
        for (int i = 0; i < workers.Count; i++)
        {
            var worker = workers[i];
            tasks[i] = BuildSummaryAsync(worker, ct);
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    [Cached(DurationSeconds = 2)]
    public async Task<Result<QueueSummary>> HandleAsync(GetQueue query, CancellationToken ct)
    {
        var worker = _registry.GetWorker(query.QueueName);
        if (worker is null)
            return Result.NotFound($"Queue worker '{query.QueueName}' not found");

        return await BuildSummaryAsync(worker, ct).ConfigureAwait(false);
    }

    public async Task<Result<JobDashboardView>> HandleAsync(GetJobDashboard query, CancellationToken ct)
    {
        if (_stateStore is null)
            return Result.Error("Job state tracking is not configured.");

        var queuedCount = await _stateStore.GetJobCountByStatusAsync(query.QueueName, QueueJobStatus.Queued, ct).ConfigureAwait(false);

        var activeJobs = await _stateStore.GetJobsByStatusAsync(
            query.QueueName, QueueJobStatus.Processing, 0, 200, ct).ConfigureAwait(false);

        var recentTerminalCount = query.RecentTerminalCount ?? 20;
        var completedJobs = await _stateStore.GetJobsByStatusAsync(query.QueueName, QueueJobStatus.Completed, 0, recentTerminalCount, ct).ConfigureAwait(false);
        var failedJobs = await _stateStore.GetJobsByStatusAsync(query.QueueName, QueueJobStatus.Failed, 0, recentTerminalCount, ct).ConfigureAwait(false);
        var cancelledJobs = await _stateStore.GetJobsByStatusAsync(query.QueueName, QueueJobStatus.Cancelled, 0, recentTerminalCount, ct).ConfigureAwait(false);

        var recentJobs = completedJobs.Concat(failedJobs).Concat(cancelledJobs)
            .OrderByDescending(j => j.CompletedUtc ?? j.LastUpdatedUtc)
            .Take(recentTerminalCount)
            .ToList();

        CounterStatsView? counterStats = null;
        try
        {
            var stats = await _stateStore.GetCounterStatsAsync(query.QueueName, TimeSpan.FromHours(24), ct).ConfigureAwait(false);
            counterStats = new CounterStatsView
            {
                Totals = stats.Totals,
                Buckets = stats.Buckets.Select(b => new CounterBucketView
                {
                    Hour = b.Hour,
                    Counters = b.Counters
                }).ToList()
            };
        }
        catch { /* State store may not support counters */ }

        return new JobDashboardView
        {
            QueuedCount = queuedCount,
            ActiveJobs = activeJobs.Select(ToJobSummary).ToList(),
            RecentJobs = recentJobs.Select(ToJobSummary).ToList(),
            CounterStats = counterStats
        };
    }

    public async Task<Result<JobSummary>> HandleAsync(GetQueueJobDetail query, CancellationToken ct)
    {
        if (_stateStore is null)
            return Result.Error("Job state tracking is not configured.");

        var state = await _stateStore.GetJobStateAsync(query.JobId, ct).ConfigureAwait(false);
        if (state is null)
            return Result.NotFound($"Job '{query.JobId}' not found");

        return ToJobSummary(state);
    }

    public async Task<Result<JobCancellationResult>> HandleAsync(CancelJob command, CancellationToken ct)
    {
        if (_stateStore is null)
            return Result.Error("Job state tracking is not configured.");

        var requested = await _stateStore.RequestCancellationAsync(command.JobId, ct).ConfigureAwait(false);
        if (!requested)
            return Result.NotFound($"Job '{command.JobId}' not found or already in a terminal state");

        return new JobCancellationResult(command.JobId, true);
    }

    public async Task<Result<DemoJobEnqueued>> HandleAsync(EnqueueDemoJob command, IMediator mediator, CancellationToken ct)
    {
        var count = Math.Clamp(command.Count, 1, 100);
        string? lastJobId = null;

        for (int i = 0; i < count; i++)
        {
            var result = await mediator.InvokeAsync<Result>(new DemoExportJob(command.Steps, command.StepDelayMs), ct);
            if (result.Status == ResultStatus.Accepted && !string.IsNullOrEmpty(result.Message))
                lastJobId = result.Message;
        }

        return new DemoJobEnqueued(lastJobId ?? string.Empty);
    }

    private async Task<QueueSummary> BuildSummaryAsync(QueueWorkerInfo worker, CancellationToken ct)
    {
        // Wait for queues to be created before hitting SQS for stats.
        // Without this, cold-start dashboard requests trigger slow/failing
        // GetQueueAttributes calls for queues that don't exist yet.
        if (_infraReady is not null)
            await _infraReady.WaitAsync(ct).ConfigureAwait(false);

        QueueStats? stats = null;
        try { stats = await _queueClient.GetQueueStatsAsync(worker.QueueName, ct).ConfigureAwait(false); }
        catch { /* Transport may not support stats */ }

        return await ToSummaryAsync(worker, stats, ct).ConfigureAwait(false);
    }

    private async Task<QueueSummary> ToSummaryAsync(QueueWorkerInfo worker, QueueStats? stats, CancellationToken ct)
    {
        QueueCounterStats? counterStats = null;
        long? processingCount = null;
        if (_stateStore is not null)
        {
            try { counterStats = await _stateStore.GetCounterStatsAsync(worker.QueueName, TimeSpan.FromHours(24), ct).ConfigureAwait(false); }
            catch { /* State store may not be available */ }

            if (worker.TrackProgress)
            {
                try { processingCount = await _stateStore.GetJobCountByStatusAsync(worker.QueueName, QueueJobStatus.Processing, ct).ConfigureAwait(false); }
                catch { /* State store may not be available */ }
            }
        }

        CounterStatsView? counterStatsView = null;
        if (counterStats is not null)
        {
            counterStatsView = new CounterStatsView
            {
                Totals = counterStats.Totals,
                Buckets = counterStats.Buckets.Select(b => new CounterBucketView
                {
                    Hour = b.Hour,
                    Counters = b.Counters
                }).ToList()
            };
        }

        return new QueueSummary
        {
            QueueName = worker.QueueName,
            MessageType = worker.MessageTypeName,
            Concurrency = worker.Concurrency,
            MaxAttempts = worker.MaxAttempts,
            RetryPolicy = worker.RetryPolicy.ToString(),
            TrackProgress = worker.TrackProgress,
            Description = worker.Description,
            IsRunning = worker.WorkerRegistered ? worker.IsRunning : null,
            MessagesProcessed = counterStats?.Totals.GetValueOrDefault("processed") ?? worker.MessagesProcessed,
            MessagesFailed = counterStats?.Totals.GetValueOrDefault("failed") ?? worker.MessagesFailed,
            MessagesDeadLettered = counterStats?.Totals.GetValueOrDefault("dead_lettered") ?? worker.MessagesDeadLettered,
            ActiveCount = stats?.ActiveCount ?? 0,
            DeadLetterCount = counterStats?.Totals.GetValueOrDefault("dead_lettered") ?? stats?.DeadLetterCount ?? 0,
            InFlightCount = processingCount ?? stats?.InFlightCount ?? 0,
            CounterStats = counterStatsView
        };
    }

    private static JobSummary ToJobSummary(QueueJobState s) => new()
    {
        JobId = s.JobId,
        QueueName = s.QueueName,
        MessageType = s.MessageType,
        Status = s.Status.ToString(),
        Progress = s.Progress,
        ProgressMessage = s.ProgressMessage,
        Attempt = s.Attempt,
        CreatedUtc = s.CreatedUtc,
        StartedUtc = s.StartedUtc,
        CompletedUtc = s.CompletedUtc,
        ErrorMessage = s.ErrorMessage
    };
}
