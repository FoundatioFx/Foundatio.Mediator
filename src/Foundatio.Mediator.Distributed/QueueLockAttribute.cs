using Foundatio.Mediator;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Coordinates queued executions that share a resource key while the lock is owned.
/// Handlers must still be idempotent: locks do not provide persistent duplicate detection.
/// </summary>
/// <remarks>
/// The lock key is, in order: <see cref="Key"/>, the message's <see cref="IHaveLockKey.GetLockKey"/>, or the
/// queue name plus message id. Contention waits with jitter while renewing the queue lease,
/// without consuming another handler attempt. Requires an <see cref="IQueueLockProvider"/>.
/// </remarks>
[UseMiddleware(typeof(QueueLockMiddleware))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class QueueLockAttribute : Attribute
{
    /// <summary>
    /// A fixed lock key shared by every message on the handler, for example a resource name.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Lock lifetime in seconds. It is renewed while the handler runs. Defaults to the queue's visibility timeout.
    /// </summary>
    public int LifetimeSeconds { get; set; }

    /// <summary>
    /// Maximum wait in each acquisition call. Zero (the default) probes immediately; contention
    /// is retried with jitter until acquisition or cancellation.
    /// </summary>
    public int AcquireTimeoutSeconds { get; set; }
}

/// <summary>
/// Implemented by messages that supply their own lock key for <see cref="QueueLockAttribute"/>.
/// </summary>
public interface IHaveLockKey
{
    /// <summary>Returns the resource key without adding a serialized message property.</summary>
    string GetLockKey();
}
