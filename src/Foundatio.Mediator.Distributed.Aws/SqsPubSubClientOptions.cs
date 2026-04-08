namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Options for configuring the SNS+SQS pub/sub client.
/// </summary>
public class SnsSqsPubSubClientOptions
{
    /// <summary>
    /// The SNS topic name. This is used to create or look up the topic.
    /// When <see cref="TopicArn"/> is set, this is ignored.
    /// Default is <c>"distributed-notifications"</c>.
    /// </summary>
    public string TopicName { get; set; } = "distributed-notifications";

    /// <summary>
    /// When set, the topic ARN is used directly instead of creating/looking up by name.
    /// </summary>
    public string? TopicArn { get; set; }

    /// <summary>
    /// When true, the SNS topic and per-node SQS queue are automatically created if they
    /// do not exist. Default is true. Disable in production where infrastructure is
    /// provisioned via IaC.
    /// </summary>
    public bool AutoCreate { get; set; } = true;

    /// <summary>
    /// Prefix for the per-node SQS queue name. The queue is named
    /// <c>{QueuePrefix}-{HostId}</c>. Default is <c>"notifications"</c>.
    /// </summary>
    public string QueuePrefix { get; set; } = "notifications";

    /// <summary>
    /// SQS long-poll wait time in seconds. Default is 20 (maximum).
    /// </summary>
    public int WaitTimeSeconds { get; set; } = 20;

    /// <summary>
    /// When true, the per-node SQS queue and SNS subscription are deleted on dispose.
    /// Default is true.
    /// </summary>
    public bool CleanupOnDispose { get; set; } = true;
}
