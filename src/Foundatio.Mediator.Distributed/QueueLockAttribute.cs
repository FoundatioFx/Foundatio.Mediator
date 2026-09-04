using Foundatio.Mediator;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Serializes queued handler executions on a distributed lock so a redelivered or duplicated message
/// never runs concurrently with, or after, another worker's execution of the same work.
/// </summary>
/// <remarks>
/// The lock key is, in order: <see cref="Key"/>, the message's <see cref="IHaveLockKey.LockKey"/>, or the
/// queue name plus message id. When the lock is already held the message is completed without running
/// the handler, because another worker owns that work. Requires an <see cref="IQueueLockProvider"/>.
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
    /// How long to wait for a held lock before giving up. Zero (the default) fails fast.
    /// </summary>
    public int AcquireTimeoutSeconds { get; set; }
}

/// <summary>
/// Implemented by messages that supply their own lock key for <see cref="QueueLockAttribute"/>.
/// </summary>
public interface IHaveLockKey
{
    string LockKey { get; }
}
