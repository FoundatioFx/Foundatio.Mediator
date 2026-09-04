namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Options for configuring the SNS+SQS pub/sub client. Every node publishes to one SNS topic and
/// receives through its own short-lived SQS queue named <c>{QueuePrefix}-{HostId}</c>.
/// </summary>
/// <remarks>
/// <para>IAM actions required by role:</para>
/// <list type="bullet">
/// <item><description><b>Publish-only</b>: <c>sns:Publish</c> (plus <c>sns:CreateTopic</c> when <see cref="AutoCreate"/> is on, or
/// <c>sns:ListTopics</c> when it is off and <see cref="TopicArn"/> is not set).</description></item>
/// <item><description><b>Subscriber node</b>: the publish actions plus <c>sqs:CreateQueue</c>, <c>sqs:GetQueueUrl</c>, <c>sqs:GetQueueAttributes</c>,
/// <c>sqs:SetQueueAttributes</c>, <c>sqs:TagQueue</c>, <c>sqs:ReceiveMessage</c>, <c>sqs:DeleteMessage</c>, <c>sqs:DeleteQueue</c>,
/// <c>sns:Subscribe</c>, <c>sns:Unsubscribe</c>; and for sweeping crashed nodes' queues <c>sqs:ListQueues</c>, <c>sqs:ListQueueTags</c>,
/// <c>sns:ListSubscriptionsByTopic</c>.</description></item>
/// </list>
/// <para>Per-node queues are always created by the node itself, so subscriber nodes need the queue actions even in production.</para>
/// </remarks>
public class SqsPubSubClientOptions
{
    /// <summary>
    /// When set, the topic ARN is used directly instead of creating/looking up the topic by name.
    /// </summary>
    public string? TopicArn { get; set; }

    /// <summary>
    /// When true, the SNS topic is created if it does not exist. Default is true. When false the topic
    /// is looked up by name (or <see cref="TopicArn"/> is used) and startup fails if it is missing.
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
    /// When true, the per-node SQS queue and SNS subscription are deleted on dispose. Default is true.
    /// </summary>
    public bool CleanupOnDispose { get; set; } = true;

    /// <summary>
    /// Message retention of the per-node subscription queue. Notifications are only useful while the node
    /// is alive, so this is short. Default is 5 minutes; SQS allows 1 minute to 14 days.
    /// </summary>
    public TimeSpan SubscriptionQueueRetention { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often the node refreshes the <c>fm:heartbeat</c> tag on its subscription queue. Default is 2 minutes.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A subscription queue whose heartbeat is older than this is treated as left behind by a crashed node
    /// and is deleted, together with its SNS subscriptions, by the next node that starts. Default is 10 minutes;
    /// keep it well above <see cref="HeartbeatInterval"/>.
    /// </summary>
    public TimeSpan StaleSubscriptionAge { get; set; } = TimeSpan.FromMinutes(10);
}
