using Foundatio.Jobs;
using Foundatio.Messaging;
using Common.Module.Messages;
using Common.Module.Middleware;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;

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
    IJobMonitor? jobMonitor = null,
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
        if (jobMonitor is null)
            return Result.Invalid("Job tracking is not configured; register an IJobRuntimeStore.");
        if (topology.GetByQueueName(query.QueueName) is not { Settings.TrackProgress: true })
            return Result.NotFound("This queue is not registered for job tracking.");
        if (query.Skip is < 0 or > 1000 || query.Take is < 1 or > 50)
            return Result.Invalid("Choose a page size from 1 to 50 and an offset from 0 to 1000.");

        var allStatuses = Enum.GetValues<JobStatus>();
        JobStatus[] statuses;
        if (query.Status == "active")
            statuses = [JobStatus.Queued, JobStatus.Processing, JobStatus.RetryPending, JobStatus.EnqueueUnknown];
        else if (query.Status == "all")
            statuses = allStatuses;
        else if (Enum.TryParse<JobStatus>(query.Status, out var status) && Enum.IsDefined(status))
            statuses = [status];
        else
            return Result.Invalid("Choose active, all, or a supported job status.");

        var counts = await Task.WhenAll(allStatuses.Select(async status => new KeyValuePair<string, long>(status.ToString(),
            await jobMonitor.CountAsync(new JobQuery { QueueName = query.QueueName, Status = status }, ct).ConfigureAwait(false)))).ConfigureAwait(false);
        // Each status index is newest first. Merge with processing jobs first before taking the page.
        var pages = await Task.WhenAll(statuses.Select(async status =>
        {
            int count = query.Skip + query.Take;
            var filter = new JobQuery { QueueName = query.QueueName, Status = status, NewestFirst = true, Limit = Math.Min(count, 1000) };
            var first = await jobMonitor.QueryAsync(filter, ct).ConfigureAwait(false);
            if (count <= 1000) return first.ToArray();
            var remaining = await jobMonitor.QueryAsync(filter with { Skip = 1000, Limit = count - 1000 }, ct).ConfigureAwait(false);
            return first.Concat(remaining).ToArray();
        })).ConfigureAwait(false);
        var jobs = pages.SelectMany(page => page).DistinctBy(job => job.JobId)
            .OrderByDescending(job => job.Status == JobStatus.Processing)
            .ThenByDescending(job => job.CreatedUtc).ThenByDescending(job => job.JobId, StringComparer.Ordinal).Skip(query.Skip).Take(query.Take).ToArray();
        return new JobDashboardView
        {
            Counts = counts.ToDictionary(),
            Jobs = jobs.Select(ToJobSummary).ToArray(),
            Total = counts.Where(count => statuses.Any(status => status.ToString() == count.Key)).Sum(count => count.Value),
            Skip = query.Skip,
            Take = query.Take,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    [HandlerAllowAnonymous]
    public async Task<Result<JobSummary>> HandleAsync(GetQueueJobDetail query, IMediator mediator, CancellationToken ct)
    {
        var job = await mediator.InvokeAsync<Result<JobState>>(new GetQueueJob(query.JobId), ct);
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
        => await mediator.InvokeAsync<Result<DeadLetterPurgeResult>>(new PurgeDeadLetters(command.QueueName, command.Max, command.MessageId), ct);

    /// <summary>
    /// Invoking a <c>[Queue]</c> handler enqueues instead of running; the job id of a tracked job comes back in
    /// the typed <see cref="QueueReceipt"/>.
    /// </summary>
    [HandlerAuthorize(Roles = ["Admin"])]
    [HandlerEndpoint(HandlerMethod.Post, "enqueue/exports")]
    public Task<Result<EnqueueReceipt>> HandleAsync(EnqueueDemoJob command, IMediator mediator, CancellationToken ct)
        => command.Count is < 1 or > 100
            ? Task.FromResult<Result<EnqueueReceipt>>(Result.Invalid("Choose between 1 and 100 exports."))
            : EnqueueAsync<DemoExportJob>(mediator, command.Count, _ => new DemoExportJob(command.Steps, command.StepDelayMs, command.FailTimes, command.CriticalFailure), ct);

    [HandlerAuthorize(Roles = ["Admin"])]
    [HandlerEndpoint(HandlerMethod.Post, "enqueue/imports")]
    public Task<Result<EnqueueReceipt>> HandleAsync(EnqueueImportJob command, IMediator mediator, CancellationToken ct)
        => EnqueueAsync<ImportProductCatalog>(mediator, Math.Clamp(command.Count, 1, 20), _ => new ImportProductCatalog(command.Rows, command.RowDelayMs), ct);

    [HandlerAuthorize(Roles = ["Admin"])]
    [HandlerEndpoint(HandlerMethod.Post, "enqueue/flaky-webhook")]
    public Task<Result<EnqueueReceipt>> HandleAsync(EnqueueFlakyWebhook command, IMediator mediator, CancellationToken ct)
        => EnqueueAsync<DeliverWebhook>(mediator, 1, _ => new DeliverWebhook(command.Url, Math.Max(0, command.FailTimes)), ct);

    /// <summary>Two files for one bank land on the queue together; <c>[QueueLock]</c> runs both sequentially while the resource lock is held.</summary>
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
            var result = await mediator.EnqueueAsync(create(i), ct);
            if (!result.IsSuccess)
                return Result<EnqueueReceipt>.FromResult(result);

            if (!string.IsNullOrEmpty(result.Value.JobId))
                jobIds.Add(result.Value.JobId);
        }

        return new EnqueueReceipt(QueueNameFor<TMessage>(), count, jobIds);
    }

    private string QueueNameFor<TMessage>()
        => topology.Queues.FirstOrDefault(q => q.MessageType == typeof(TMessage))?.QueueName ?? typeof(TMessage).Name;

    private Task WaitForInfrastructureAsync(CancellationToken ct)
        => infraReady?.WaitAsync(ct) ?? Task.CompletedTask;

    private QueueSummary ToSummary(QueueOverview q) => new()
    {
        QueueName = q.QueueName,
        DisplayName = q.DisplayName,
        MessageType = q.MessageType,
        Handlers = q.Handlers,
        Group = q.Group,
        Description = q.Description,
        Concurrency = q.Concurrency,
        PrefetchCount = topology.GetByQueueName(q.QueueName)!.Settings.PrefetchCount > 0 ? topology.GetByQueueName(q.QueueName)!.Settings.PrefetchCount : q.Concurrency,
        AutoComplete = topology.GetByQueueName(q.QueueName)!.Settings.AutoComplete,
        AutoRenewTimeout = topology.GetByQueueName(q.QueueName)!.Settings.AutoRenewTimeout,
        RetryDelays = topology.GetByQueueName(q.QueueName)!.Settings.RetryDelays,
        MaxAttempts = q.MaxAttempts,
        RetryPolicy = q.RetryPolicy,
        VisibilityTimeoutSeconds = (int)q.VisibilityTimeout.TotalSeconds,
        TrackProgress = q.TrackProgress,
        WorkerRunsHere = q.WorkerRunsHere,
        IsRunning = q.IsRunning,
        MessagesProcessed = q.Processed,
        MessagesFailed = q.Failed,
        MessagesDeadLettered = q.DeadLettered,
        StatisticsAvailable = q.StatisticsAvailable,
        ActiveCount = q.ActiveCount,
        DelayedCount = q.DelayedCount,
        InFlightCount = q.InFlightCount,
        DeadLetterCount = q.DeadLetterCount,
        CounterStats = q.Counters is null ? null : ToCounterStats(q.Counters)
    };

    private static CounterStatsView ToCounterStats(JobCounterStats stats) => new()
    {
        Totals = stats.Totals,
        Buckets = stats.Buckets.Select(b => new CounterBucketView { Hour = b.Hour, Counters = b.Counters }).ToList()
    };

    private static JobSummary ToJobSummary(JobState s) => new()
    {
        JobId = s.JobId,
        QueueName = s.QueueName ?? s.Name,
        MessageType = s.PayloadType ?? s.JobType ?? s.Name,
        Status = s.Status.ToString(),
        Progress = s.Progress ?? 0,
        ProgressMessage = s.ProgressMessage,
        Attempt = s.Attempt,
        WorkerId = s.NodeId,
        LastUpdatedUtc = s.LastUpdatedUtc,
        CancellationRequested = s.CancellationRequested,
        CreatedUtc = s.CreatedUtc,
        StartedUtc = s.StartedUtc,
        CompletedUtc = s.CompletedUtc,
        LastHeartbeatUtc = s.LastHeartbeatUtc,
        ErrorMessage = s.Error,
        Metadata = s.Metadata
    };
}
