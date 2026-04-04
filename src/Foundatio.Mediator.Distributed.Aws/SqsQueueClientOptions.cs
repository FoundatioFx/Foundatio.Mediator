namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Options for configuring the SQS queue client.
/// </summary>
public class SqsQueueClientOptions
{
    /// <summary>
    /// When true, queues are automatically created if they do not exist.
    /// Default is true (convenient for dev/test). Disable in production where
    /// queues are provisioned via IaC.
    /// </summary>
    public bool AutoCreateQueues { get; set; } = true;

    /// <summary>
    /// SQS long-poll wait time in seconds. Default is 20 (maximum).
    /// Set to 0 for short polling.
    /// </summary>
    public int WaitTimeSeconds { get; set; } = 20;

    /// <summary>
    /// Default visibility timeout in seconds for received messages. Default is 30.
    /// Can be overridden per-queue via <see cref="QueueAttribute.Timeout"/>.
    /// </summary>
    public int DefaultVisibilityTimeoutSeconds { get; set; } = 30;
}
