using Common.Module.Messages;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;

namespace Common.Module.Handlers;

/// <summary>
/// Queue dashboard handler — exposes queue workers, job tracking, and cancellation
/// as mediator endpoints under <c>/api/queues</c>.
/// </summary>
[HandlerEndpointGroup("Queues")]
[HandlerAllowAnonymous]
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

    public async Task<Result<List<QueueSummary>>> HandleAsync(GetQueues query, CancellationToken ct)
    {
        var workers = _registry.GetWorkers();
        var results = new List<QueueSummary>(workers.Count);

        foreach (var worker in workers)
        {
            QueueStats? stats = null;
            try { stats = await _queueClient.GetQueueStatsAsync(worker.QueueName, ct).ConfigureAwait(false); }
            catch { /* Transport may not support stats */ }

            results.Add(await ToSummaryAsync(worker, stats, ct).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<Result<QueueSummary>> HandleAsync(GetQueue query, CancellationToken ct)
    {
        var worker = _registry.GetWorker(query.QueueName);
        if (worker is null)
            return Result.NotFound($"Queue worker '{query.QueueName}' not found");

        QueueStats? stats = null;
        try { stats = await _queueClient.GetQueueStatsAsync(query.QueueName, ct).ConfigureAwait(false); }
        catch { /* Transport may not support stats */ }

        return await ToSummaryAsync(worker, stats, ct).ConfigureAwait(false);
    }

    public async Task<Result<JobDashboardView>> HandleAsync(GetJobDashboard query, CancellationToken ct)
    {
        if (_stateStore is null)
            return Result.Error("Job state tracking is not configured.");

        var queuedCount = await _stateStore.GetJobCountByStatusAsync(query.QueueName, QueueJobStatus.Queued, ct).ConfigureAwait(false);

        var activeJobs = await _stateStore.GetJobsByStatusAsync(
            query.QueueName, [QueueJobStatus.Processing], 0, 200, ct).ConfigureAwait(false);

        var recentJobs = await _stateStore.GetJobsByStatusAsync(
            query.QueueName, [QueueJobStatus.Completed, QueueJobStatus.Failed, QueueJobStatus.Cancelled],
            0, query.RecentTerminalCount ?? 20, ct).ConfigureAwait(false);

        return new JobDashboardView
        {
            QueuedCount = queuedCount,
            ActiveJobs = activeJobs.Select(ToJobSummary).ToList(),
            RecentJobs = recentJobs.Select(ToJobSummary).ToList()
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

    private async Task<QueueSummary> ToSummaryAsync(QueueWorkerInfo worker, QueueStats? stats, CancellationToken ct)
    {
        IReadOnlyDictionary<string, long>? counters = null;
        long? processingCount = null;
        if (_stateStore is not null)
        {
            try { counters = await _stateStore.GetCountersAsync(worker.QueueName, ct).ConfigureAwait(false); }
            catch { /* State store may not be available */ }

            if (worker.TrackProgress)
            {
                try { processingCount = await _stateStore.GetJobCountByStatusAsync(worker.QueueName, QueueJobStatus.Processing, ct).ConfigureAwait(false); }
                catch { /* State store may not be available */ }
            }
        }

        return new QueueSummary
        {
            QueueName = worker.QueueName,
            MessageType = worker.MessageTypeName,
            Concurrency = worker.Concurrency,
            MaxRetries = worker.MaxRetries,
            RetryPolicy = worker.RetryPolicy.ToString(),
            TrackProgress = worker.TrackProgress,
            IsRunning = worker.IsRunning,
            MessagesProcessed = counters?.GetValueOrDefault("processed") ?? worker.MessagesProcessed,
            MessagesFailed = counters?.GetValueOrDefault("failed") ?? worker.MessagesFailed,
            MessagesDeadLettered = counters?.GetValueOrDefault("dead_lettered") ?? worker.MessagesDeadLettered,
            ActiveCount = stats?.ActiveCount ?? 0,
            DeadLetterCount = stats?.DeadLetterCount ?? 0,
            InFlightCount = processingCount ?? stats?.InFlightCount ?? 0
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
        CreatedUtc = s.CreatedUtc,
        StartedUtc = s.StartedUtc,
        CompletedUtc = s.CompletedUtc,
        ErrorMessage = s.ErrorMessage
    };
}
