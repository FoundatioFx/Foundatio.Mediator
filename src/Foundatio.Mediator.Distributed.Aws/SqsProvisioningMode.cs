namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Controls what <see cref="SqsQueueClient.EnsureQueuesAsync"/> does with the queues the
/// application declares at startup.
/// </summary>
public enum SqsProvisioningMode
{
    /// <summary>
    /// Creates every missing queue and dead-letter queue with the attributes from its
    /// <see cref="QueueDefinition"/> and updates the attributes of existing queues. Queues that
    /// are used without a definition are created lazily on first use. Requires the provisioning
    /// IAM actions listed on <see cref="SqsQueueClientOptions"/>.
    /// </summary>
    Create,

    /// <summary>
    /// Requires every queue and dead-letter queue to already exist with matching attributes.
    /// Startup fails with a single exception that lists every missing queue and every attribute
    /// mismatch. Never creates anything.
    /// </summary>
    Validate,

    /// <summary>
    /// Never creates or validates queues; URLs are resolved lazily on first use. Use this when
    /// queues are provisioned elsewhere and the process should not spend startup calls on them.
    /// </summary>
    None
}
