using System.Text.Json;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Process-wide options for distributed queues.
/// </summary>
public class DistributedQueueOptions
{
    /// <summary>
    /// Serializer options for message bodies. Defaults to <see cref="JsonSerializerOptions.Default"/>.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// When set, only workers whose <see cref="QueueAttribute.Group"/> matches start in this process.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// When <c>false</c>, this process only enqueues; no workers start.
    /// </summary>
    public bool WorkersEnabled { get; set; } = true;

    /// <summary>
    /// When set, only workers whose queue name or group is in this set start in this process.
    /// </summary>
    public HashSet<string>? Queues { get; set; }

    /// <summary>
    /// Prefix applied to every queue name, for example an environment or tenant scope.
    /// </summary>
    public string? ResourcePrefix { get; set; }

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

    /// <summary>
    /// Allows an enqueue-only or filtered process to keep the in-memory queue client. Off by default
    /// because messages enqueued to an in-memory queue with no worker in the same process are lost.
    /// </summary>
    public bool AllowInMemoryWithoutWorkers { get; set; }

    /// <summary>
    /// Produces <see cref="QueueJobState.Metadata"/> for tracked jobs from the message being enqueued,
    /// for example a tenant or requesting-user id that a job state store can index on.
    /// </summary>
    public Func<object, IReadOnlyDictionary<string, string>?>? JobMetadataProvider { get; set; }

    /// <summary>
    /// Applies <see cref="ResourcePrefix"/> to a queue name.
    /// </summary>
    public string ApplyPrefix(string name) =>
        string.IsNullOrEmpty(ResourcePrefix) ? name : $"{ResourcePrefix}-{name}";
}
