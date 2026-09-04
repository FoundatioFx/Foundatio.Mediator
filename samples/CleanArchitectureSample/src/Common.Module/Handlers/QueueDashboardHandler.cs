using Common.Module.Messages;
using Common.Module.Middleware;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// The sample's HTTP surface for queue operations under <c>/api/queues</c>. The library's administration
/// handlers (<see cref="GetQueueOverview"/>, <see cref="ListDeadLetters"/>, <see cref="ReplayDeadLetters"/>, ...)
/// have no endpoints of their own; this handler delegates to them through the mediator and adds the host's
/// authorization: reads are anonymous, anything that changes state needs the Admin role.
/// </summary>
[HandlerEndpointGroup("Queues")]
public class QueueDashboardHandler(
    QueueTopology topology,
    DistributedQueueOptions queueOptions,
    HostInfo host,
    ILogger<QueueDashboardHandler> logger,
    IQueueJobStateStore? stateStore = null,
    DistributedInfrastructureReady? infraReady = null)
{
    // ── Reads ──

    [HandlerAllowAnonymous]
    [Cached(DurationSeconds = 2)]
    public async Task<Result<List<QueueSummary>>> HandleAsync(GetQueues query, IMediator mediator, CancellationToken ct)
    {
        await WaitForInfrastructureAsync(ct).ConfigureAwait(false);

        var overview = await mediator.InvokeAsync<Result<IReadOnlyList<QueueOverview>>>(new GetQueueOverview(), ct);
        if (!overview.IsSuccess)
            return Result<List<QueueSummary>>.FromResult(overview);

        return overview.Value!.Select(ToSummary).ToList();
    }

    [HandlerAllowAnonymous]
    [Cached(DurationSeconds = 2)]
    public async Task<Result<QueueSummary>> HandleAsync(GetQueue query, IMediator mediator, CancellationToken ct)
    {
        await WaitForInfrastructureAsync(ct).ConfigureAwait(false);

        var detail = await mediator.InvokeAsync<Result<QueueOverview>>(new GetQueueDetail(query.QueueName), ct);
        return detail.IsSuccess ? ToSummary(detail.Value!) : Result<QueueSummary>.FromResult(detail);
    }

    [HandlerAllowAnonymous]
    public async Task<Result<JobDashboardView>> HandleAsync(GetJobDashboard query, CancellationToken ct)
    {
        if (stateStore is null)
            return Result.Invalid("Job tracking is not configured; register an IQueueJobStateStore.");

        var queuedCount = await stateStore.GetJobCountByStatusAsync(query.QueueName, QueueJobStatus.Queued, ct).ConfigureAwait(false);
        var activeJobs = await stateStore.GetJobsByStatusAsync(query.QueueName, QueueJobStatus.Processing, 0, 200, ct).ConfigureAwait(false);

        var recentTerminalCount = query.RecentTerminalCount ?? 20;
        var completedJobs = await stateStore.GetJobsByStatusAsync(query.QueueName, QueueJobStatus.Completed, 0, recentTerminalCount, ct).ConfigureAwait(false);
        var failedJobs = await stateStore.GetJobsByStatusAsync(query.QueueName, QueueJobStatus.Failed, 0, recentTerminalCount, ct).ConfigureAwait(false);
        var cancelledJobs = await stateStore.GetJobsByStatusAsync(query.QueueName, QueueJobStatus.Cancelled, 0, recentTerminalCount, ct).ConfigureAwait(false);

        var recentJobs = completedJobs.Concat(failedJobs).Concat(cancelledJobs)
            .OrderByDescending(j => j.CompletedUtc ?? j.LastUpdatedUtc)
            .Take(recentTerminalCount)
            .ToList();

        CounterStatsView? counterStats = null;
        try
        {
            counterStats = ToCounterStats(await stateStore.GetCounterStatsAsync(query.QueueName, TimeSpan.FromHours(24), ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read counters for {QueueName}", query.QueueName);
        }

        return new JobDashboardView
        {
            QueuedCount = queuedCount,
            ActiveJobs = activeJobs.Select(ToJobSummary).ToList(),
            RecentJobs = recentJobs.Select(ToJobSummary).ToList(),
            CounterStats = counterStats
        };
    }

    [HandlerAllowAnonymous]
    public async Task<Result<JobSummary>> HandleAsync(GetQueueJobDetail query, IMediator mediator, CancellationToken ct)
    {
        var job = await mediator.InvokeAsync<Result<QueueJobState>>(new GetQueueJob(query.JobId), ct);
        return job.IsSuccess ? ToJobSummary(job.Value!) : Result<JobSummary>.FromResult(job);
    }

    [HandlerAllowAnonymous]
    [HandlerEndpoint(HandlerMethod.Get, "dead-letters")]
    public async Task<Result<IReadOnlyList<DeadLetterView>>> HandleAsync(GetDeadLetters query, IMediator mediator, CancellationToken ct)
    {
        await WaitForInfrastructureAsync(ct).ConfigureAwait(false);
        return await mediator.InvokeAsync<Result<IReadOnlyList<DeadLetterView>>>(new ListDeadLetters(query.QueueName, query.Take), ct);
    }

    [HandlerAllowAnonymous]
    [HandlerEndpoint(HandlerMethod.Get, "host")]
    public HostInfoView Handle(GetHostInfo query) => new(host.HostId, queueOptions.Workers.ToString());

    // ── Operations ──

    [HandlerAuthorize(Roles = ["Admin"])]
    public async Task<Result<QueueJobCancellation>> HandleAsync(CancelJob command, IMediator mediator, CancellationToken ct)
        => await mediator.InvokeAsync<Result<QueueJobCancellation>>(new CancelQueueJob(command.JobId), ct);

    [HandlerAuthorize(Roles = ["Admin"])]
    [HandlerEndpoint(HandlerMethod.Post, "dead-letters/replay")]
    public async Task<Result<DeadLetterReplayResult>> HandleAsync(ReplayQueueDeadLetters command, IMediator mediator, CancellationToken ct)
        => await mediator.InvokeAsync<Result<DeadLetterReplayResult>>(new ReplayDeadLetters(command.QueueName, command.Max, command.MessageId), ct);

    [HandlerAuthorize(Roles = ["Admin"])]
    [HandlerEndpoint(HandlerMethod.Post, "dead-letters/purge")]
    public async Task<Result<DeadLetterPurgeResult>> HandleAsync(PurgeQueueDeadLetters command, IMediator mediator, CancellationToken ct)
        => await mediator.InvokeAsync<Result<DeadLetterPurgeResult>>(new PurgeDeadLetters(command.QueueName, command.Max), ct);

    /// <summary>
    /// Invoking a <c>[Queue]</c> handler enqueues instead of running; the job id of a tracked job comes back in
    /// <see cref="Result.Location"/>.
    /// </summary>
    [HandlerAuthorize(Roles = ["Admin"])]
    [HandlerEndpoint(HandlerMethod.Post, "enqueue/exports")]
    public Task<Result<EnqueueReceipt>> HandleAsync(EnqueueDemoJob command, IMediator mediator, CancellationToken ct)
        => EnqueueAsync<DemoExportJob>(mediator, Math.Clamp(command.Count, 1, 100), _ => new DemoExportJob(command.Steps, command.StepDelayMs), ct);

    [HandlerAuthorize(Roles = ["Admin"])]
    [HandlerEndpoint(HandlerMethod.Post, "enqueue/imports")]
    public Task<Result<EnqueueReceipt>> HandleAsync(EnqueueImportJob command, IMediator mediator, CancellationToken ct)
        => EnqueueAsync<ImportProductCatalog>(mediator, Math.Clamp(command.Count, 1, 20), _ => new ImportProductCatalog(command.Rows, command.RowDelayMs), ct);

    [HandlerAuthorize(Roles = ["Admin"])]
    [HandlerEndpoint(HandlerMethod.Post, "enqueue/flaky-webhook")]
    public Task<Result<EnqueueReceipt>> HandleAsync(EnqueueFlakyWebhook command, IMediator mediator, CancellationToken ct)
        => EnqueueAsync<DeliverWebhook>(mediator, 1, _ => new DeliverWebhook(command.Url, Math.Max(0, command.FailTimes)), ct);

    /// <summary>Two files for one bank land on the queue together; <c>[QueueLock]</c> lets exactly one of them run.</summary>
    [HandlerAuthorize(Roles = ["Admin"])]
    [HandlerEndpoint(HandlerMethod.Post, "enqueue/bank-files")]
    public Task<Result<EnqueueReceipt>> HandleAsync(EnqueueBankFiles command, IMediator mediator, CancellationToken ct)
    {
        var bank = string.IsNullOrWhiteSpace(command.Bank) ? "first-national" : command.Bank.Trim().ToLowerInvariant();
        return EnqueueAsync<GenerateBankFile>(mediator, Math.Clamp(command.Count, 1, 5), _ => new GenerateBankFile(bank, Guid.NewGuid().ToString("N")), ct);
    }

    private async Task<Result<EnqueueReceipt>> EnqueueAsync<TMessage>(IMediator mediator, int count, Func<int, TMessage> create, CancellationToken ct)
        where TMessage : class
    {
        var jobIds = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var result = await mediator.InvokeAsync<Result>(create(i), ct);
            if (!result.IsSuccess)
                return Result<EnqueueReceipt>.FromResult(result);

            if (!string.IsNullOrEmpty(result.Location))
                jobIds.Add(result.Location);
        }

        return new EnqueueReceipt(QueueNameFor<TMessage>(), count, jobIds);
    }

    private string QueueNameFor<TMessage>()
        => topology.Queues.FirstOrDefault(q => q.MessageType == typeof(TMessage))?.QueueName ?? typeof(TMessage).Name;

    private Task WaitForInfrastructureAsync(CancellationToken ct)
        => infraReady?.WaitAsync(ct) ?? Task.CompletedTask;

    private static QueueSummary ToSummary(QueueOverview q) => new()
    {
        QueueName = q.QueueName,
        MessageType = q.MessageType,
        Handlers = q.Handlers,
        Group = q.Group,
        Description = q.Description,
        Concurrency = q.Concurrency,
        MaxAttempts = q.MaxAttempts,
        RetryPolicy = q.RetryPolicy,
        VisibilityTimeoutSeconds = (int)q.VisibilityTimeout.TotalSeconds,
        TrackProgress = q.TrackProgress,
        WorkerRunsHere = q.WorkerRunsHere,
        IsRunning = q.IsRunning,
        MessagesProcessed = q.Processed,
        MessagesFailed = q.Failed,
        MessagesDeadLettered = q.DeadLettered,
        ActiveCount = q.ActiveCount,
        InFlightCount = q.InFlightCount,
        DeadLetterCount = q.DeadLetterCount,
        CounterStats = q.Counters is null ? null : ToCounterStats(q.Counters)
    };

    private static CounterStatsView ToCounterStats(QueueCounterStats stats) => new()
    {
        Totals = stats.Totals,
        Buckets = stats.Buckets.Select(b => new CounterBucketView { Hour = b.Hour, Counters = b.Counters }).ToList()
    };

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
        LastHeartbeatUtc = s.LastHeartbeatUtc,
        ErrorMessage = s.ErrorMessage,
        Metadata = s.Metadata
    };
}
