namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Options for configuring the SQS queue client.
/// </summary>
/// <remarks>
/// <para>IAM actions required by role:</para>
/// <list type="bullet">
/// <item><description><b>Enqueue-only</b>: <c>sqs:SendMessage</c>, <c>sqs:GetQueueUrl</c>, <c>sqs:GetQueueAttributes</c>.</description></item>
/// <item><description><b>Worker</b>: the enqueue-only actions plus <c>sqs:ReceiveMessage</c>, <c>sqs:DeleteMessage</c>, <c>sqs:ChangeMessageVisibility</c>.</description></item>
/// <item><description><b>Provisioning</b> (<see cref="SqsProvisioningMode.Create"/> or <see cref="SqsQueueClient.ProvisionAsync"/>):
/// the worker actions plus <c>sqs:CreateQueue</c>, <c>sqs:SetQueueAttributes</c>, <c>sqs:TagQueue</c>, <c>sqs:ListQueues</c>,
/// <c>sqs:ListQueueTags</c>, <c>sqs:DeleteQueue</c>.</description></item>
/// </list>
/// <para><see cref="SqsProvisioningMode.Validate"/> only needs <c>sqs:GetQueueUrl</c> and <c>sqs:GetQueueAttributes</c> on top of the worker actions.</para>
/// </remarks>
public class SqsQueueClientOptions
{
    /// <summary>
    /// How declared queues are provisioned at startup. Default is <see cref="SqsProvisioningMode.Create"/>,
    /// which is convenient for dev/test. Use <see cref="SqsProvisioningMode.Validate"/> or
    /// <see cref="SqsProvisioningMode.None"/> in production where queues are provisioned via IaC or
    /// a one-off run of <see cref="SqsQueueClient.ProvisionAsync"/>.
    /// </summary>
    public SqsProvisioningMode Provisioning { get; set; } = SqsProvisioningMode.Create;

    /// <summary>
    /// Legacy switch mapped onto <see cref="Provisioning"/>: <c>true</c> is <see cref="SqsProvisioningMode.Create"/>,
    /// <c>false</c> is <see cref="SqsProvisioningMode.None"/>.
    /// </summary>
    [Obsolete("Use Provisioning instead. true maps to SqsProvisioningMode.Create, false to SqsProvisioningMode.None.")]
    public bool AutoCreateQueues
    {
        get => Provisioning == SqsProvisioningMode.Create;
        set => Provisioning = value ? SqsProvisioningMode.Create : SqsProvisioningMode.None;
    }

    /// <summary>
    /// Message retention applied to dead-letter queues that this client provisions or validates.
    /// Default is 14 days (the SQS maximum) so dead letters stay available for inspection and replay.
    /// </summary>
    public TimeSpan DeadLetterRetention { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// SQS long-poll wait time in seconds. Default is 20 (maximum).
    /// Set to 0 for short polling.
    /// </summary>
    public int WaitTimeSeconds { get; set; } = 20;
}
