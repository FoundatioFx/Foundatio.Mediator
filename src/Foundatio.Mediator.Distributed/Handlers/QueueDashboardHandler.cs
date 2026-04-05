namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Handles queue dashboard queries: listing workers, viewing stats, and managing tracked jobs.
/// Endpoints are auto-generated under <c>/api/queues</c> by the source generator.
/// </summary>
/// <remarks>
/// These endpoints respect the global authorization settings. To allow anonymous access
/// in development, set <c>AuthorizationRequired = false</c> on <c>[assembly: MediatorConfiguration]</c>.
/// </remarks>
[HandlerEndpointGroup("Queues")]
public class QueueDashboardHandler
{
    private readonly IQueueWorkerRegistry _registry;
    private readonly IQueueClient _queueClient;
    private readonly IQueueJobStateStore? _stateStore;

    public QueueDashboardHandler(IQueueWorkerRegistry registry, IQueueClient queueClient, IQueueJobStateStore? stateStore = null)
    {
        _registry = registry;
        _queueClient = queueClient;
        _stateStore = stateStore;
    }

    /// <summary>
    /// Gets all registered queue workers with their configuration and runtime stats.
    /// </summary>
    public async Task<Result<List<QueueWorkerSummary>>> HandleAsync(GetQueueWorkers query, CancellationToken ct)
    {
        var workers = _registry.GetWorkers();
        var results = new List<QueueWorkerSummary>(workers.Count);

        foreach (var worker in workers)
        {
            QueueStats? stats = null;
            try
            {
                stats = await _queueClient.GetQueueStatsAsync(worker.QueueName, ct).ConfigureAwait(false);
            }
            catch
            {
                // Stats may not be available for all transports
            }

            results.Add(ToSummary(worker, stats));
        }

        return results;
    }

    /// <summary>
    /// Gets a specific queue worker by queue name, including queue stats.
    /// </summary>
    public async Task<Result<QueueWorkerSummary>> HandleAsync(GetQueueWorker query, CancellationToken ct)
    {
        var worker = _registry.GetWorker(query.QueueName);
        if (worker is null)
            return Result.NotFound($"Queue worker '{query.QueueName}' not found");

        QueueStats? stats = null;
        try
        {
            stats = await _queueClient.GetQueueStatsAsync(query.QueueName, ct).ConfigureAwait(false);
        }
        catch
        {
            // Stats may not be available for all transports
        }

        return ToSummary(worker, stats);
    }

    /// <summary>
    /// Gets tracked jobs for a specific queue, ordered by creation time descending.
    /// </summary>
    public async Task<Result<IReadOnlyList<QueueJobState>>> HandleAsync(GetQueueJobs query, CancellationToken ct)
    {
        if (_stateStore is null)
            return Result.Error("Job state tracking is not configured.");

        var jobs = await _stateStore.GetJobsByQueueAsync(query.QueueName, query.Skip, query.Take, ct).ConfigureAwait(false);
        return Result<IReadOnlyList<QueueJobState>>.Ok(jobs);
    }

    /// <summary>
    /// Gets a dashboard view: queued count, active (processing) jobs, and recent terminal jobs.
    /// </summary>
    public async Task<Result<QueueJobDashboardView>> HandleAsync(GetQueueJobDashboard query, CancellationToken ct)
    {
        if (_stateStore is null)
            return Result.Error("Job state tracking is not configured.");

        var queuedCount = await _stateStore.GetJobCountByStatusAsync(query.QueueName, QueueJobStatus.Queued, ct).ConfigureAwait(false);

        var activeJobs = await _stateStore.GetJobsByStatusAsync(
            query.QueueName, [QueueJobStatus.Processing], 0, 200, ct).ConfigureAwait(false);

        var recentJobs = await _stateStore.GetJobsByStatusAsync(
            query.QueueName, [QueueJobStatus.Completed, QueueJobStatus.Failed, QueueJobStatus.Cancelled],
            0, query.RecentTerminalCount ?? 20, ct).ConfigureAwait(false);

        return new QueueJobDashboardView
        {
            QueuedCount = queuedCount,
            ActiveJobs = activeJobs,
            RecentJobs = recentJobs
        };
    }

    /// <summary>
    /// Gets the state of a specific tracked job.
    /// </summary>
    public async Task<Result<QueueJobState>> HandleAsync(GetQueueJob query, CancellationToken ct)
    {
        if (_stateStore is null)
            return Result.Error("Job state tracking is not configured.");

        var state = await _stateStore.GetJobStateAsync(query.JobId, ct).ConfigureAwait(false);
        if (state is null)
            return Result.NotFound($"Job '{query.JobId}' not found");

        return state;
    }

    /// <summary>
    /// Requests cancellation of a tracked job.
    /// </summary>
    public async Task<Result<QueueJobCancellationResult>> HandleAsync(CancelQueueJob command, CancellationToken ct)
    {
        if (_stateStore is null)
            return Result.Error("Job state tracking is not configured.");

        var requested = await _stateStore.RequestCancellationAsync(command.JobId, ct).ConfigureAwait(false);
        if (!requested)
            return Result.NotFound($"Job '{command.JobId}' not found or already in a terminal state");

        return new QueueJobCancellationResult(command.JobId, true);
    }

    private static QueueWorkerSummary ToSummary(QueueWorkerInfo worker, QueueStats? stats) => new()
    {
        QueueName = worker.QueueName,
        MessageTypeName = worker.MessageTypeName,
        Concurrency = worker.Concurrency,
        PrefetchCount = worker.PrefetchCount,
        MaxRetries = worker.MaxRetries,
        VisibilityTimeout = worker.VisibilityTimeout.ToString(),
        Group = worker.Group,
        RetryPolicy = worker.RetryPolicy.ToString(),
        TrackProgress = worker.TrackProgress,
        IsRunning = worker.IsRunning,
        MessagesProcessed = worker.MessagesProcessed,
        MessagesFailed = worker.MessagesFailed,
        MessagesDeadLettered = worker.MessagesDeadLettered,
        Stats = stats
    };
}
