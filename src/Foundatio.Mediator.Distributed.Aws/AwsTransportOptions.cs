using Amazon.Runtime;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Unified options for configuring both SQS queues and SNS/SQS pub/sub transports.
/// Use with <see cref="AwsBuilderExtensions.UseAws"/> for a single-call configuration
/// that registers both queue and notification transports.
/// </summary>
public class AwsTransportOptions
{
    /// <summary>
    /// The AWS service URL (e.g. <c>"http://localhost:4566"</c> for LocalStack).
    /// When set, the SQS and SNS SDK clients are automatically registered with this endpoint.
    /// When <c>null</c>, you must register <c>IAmazonSQS</c> and <c>IAmazonSimpleNotificationService</c>
    /// in DI before calling <c>UseAws()</c>.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// The AWS region to use when <see cref="ServiceUrl"/> is set. Default is <c>"us-east-1"</c>.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Optional AWS credentials. When <c>null</c> and <see cref="ServiceUrl"/> is set,
    /// dummy credentials (<c>"test"/"test"</c>) are used (suitable for LocalStack).
    /// When <see cref="ServiceUrl"/> is not set, this is ignored (SDK clients must be pre-registered).
    /// </summary>
    public AWSCredentials? Credentials { get; set; }

    /// <summary>
    /// Options for the SQS queue client. See <see cref="SqsQueueClientOptions"/>.
    /// </summary>
    public SqsQueueClientOptions Queues { get; set; } = new();

    /// <summary>
    /// Options for the SNS/SQS pub/sub client. See <see cref="SqsPubSubClientOptions"/>.
    /// </summary>
    public SqsPubSubClientOptions Notifications { get; set; } = new();
}
