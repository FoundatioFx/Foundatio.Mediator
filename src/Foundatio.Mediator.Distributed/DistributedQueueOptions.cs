using Foundatio.Messaging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Process-wide options for distributed queues.
/// </summary>
public class DistributedQueueOptions
{
    /// <summary>
    /// Maximum wall-clock delay to collect capacity released by concurrent broker acknowledgments
    /// before receiving again. Applies only to distributed transports under load. Default is 1 ms;
    /// zero disables coalescing. A slow handler never extends this delay.
    /// </summary>
    public TimeSpan ReceiveBatchDelay { get; set; } = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Which workers run in this process. Defaults to <see cref="WorkerSelection.All"/>. Set from
    /// configuration with <see cref="WorkerSelection.Parse"/> so the same build can be an enqueue-only
    /// API node (<c>none</c>), run everything (<c>all</c>), or run a chosen set of groups or queues.
    /// </summary>
    public WorkerSelection Workers { get; set; } = WorkerSelection.All;

    /// <summary>
    /// Prefix applied to every queue name, for example an environment or tenant scope.
    /// </summary>
    public string? ResourcePrefix { get; set; }

    /// <summary>Process identity recorded on tracked attempts. Defaults to machine name and process id.</summary>
    public string WorkerId { get; set; } = $"{Environment.MachineName}:{Environment.ProcessId}";

    /// <summary>
    /// How long in-flight handlers may keep running after the host begins stopping before they are
    /// cancelled and their messages abandoned. Keep this below the host's shutdown timeout.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long an enqueue waits for infrastructure provisioning to finish before failing.
    /// </summary>
    public TimeSpan EnqueueReadyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long job state is kept after it is written. Applied on every state update.
    /// </summary>
    public TimeSpan JobStateExpiry { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How often queue depth is sampled for the <c>queue.depth.*</c> metrics. <see cref="TimeSpan.Zero"/> disables sampling.
    /// </summary>
    public TimeSpan QueueDepthPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Operational overrides keyed by logical subscription name, before ResourcePrefix is applied.</summary>
    public IDictionary<string, Action<QueueAttribute>> QueueOverrides { get; } = new Dictionary<string, Action<QueueAttribute>>(StringComparer.OrdinalIgnoreCase);

    internal string RemovePrefix(string name) => string.IsNullOrEmpty(ResourcePrefix) ? name : name[(ResourcePrefix.Length + 1)..];

    /// <summary>
    /// Produces <see cref="MessageExecutionState.Metadata"/> for tracked jobs from the message being enqueued,
    /// for example a tenant or requesting-user id that a job state store can index on.
    /// </summary>
    public Func<object, IReadOnlyDictionary<string, string>?>? JobMetadataProvider { get; set; }

    /// <summary>
    /// Applies <see cref="ResourcePrefix"/> to a queue name.
    /// </summary>
    public string ApplyPrefix(string name) =>
        string.IsNullOrEmpty(ResourcePrefix) ? name : $"{ResourcePrefix}-{name}";
}
