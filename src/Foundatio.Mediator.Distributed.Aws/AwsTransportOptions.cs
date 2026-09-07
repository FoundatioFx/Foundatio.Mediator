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
    /// When set, the SQS and SNS SDK clients target this endpoint with <see cref="Region"/> as the signing region.
    /// When <c>null</c>, the clients come from the SDK's default credential and region chain (environment,
    /// profile, instance or task role). An <c>IAmazonSQS</c> or <c>IAmazonSimpleNotificationService</c>
    /// already registered in DI is always used as-is.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// The AWS region to use when <see cref="ServiceUrl"/> is set. Default is <c>"us-east-1"</c>.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Optional AWS credentials. When <c>null</c> and <see cref="ServiceUrl"/> is set,
    /// static test credentials (<c>"test"/"test"</c>) are used (suitable for LocalStack).
    /// When <c>null</c> and <see cref="ServiceUrl"/> is not set, the SDK's default credential chain is used.
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
