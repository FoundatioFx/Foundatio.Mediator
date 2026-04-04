using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// <see cref="IPubSubClient"/> implementation using SNS for fan-out publishing and
/// per-node SQS queues for subscription. Each subscriber creates a dedicated SQS queue
/// subscribed to the SNS topic, enabling true pub/sub fan-out across nodes.
/// </summary>
public sealed class SnsSqsPubSubClient : IPubSubClient, IAsyncDisposable
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly IAmazonSQS _sqs;
    private readonly SnsSqsPubSubClientOptions _options;
    private readonly string _hostId;
    private readonly ILogger<SnsSqsPubSubClient> _logger;
    private readonly ConcurrentDictionary<string, string> _topicArnCache = new();
    private readonly ConcurrentDictionary<string, SubscriptionSetup> _subscriptionSetupCache = new();
    private readonly ConcurrentBag<SubscriptionHandle> _activeSubscriptions = [];

    public SnsSqsPubSubClient(
        IAmazonSimpleNotificationService sns,
        IAmazonSQS sqs,
        SnsSqsPubSubClientOptions options,
        DistributedNotificationOptions notificationOptions,
        ILogger<SnsSqsPubSubClient> logger)
    {
        _sns = sns;
        _sqs = sqs;
        _options = options;
        _hostId = notificationOptions.HostId;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(string topic, PubSubEntry message, CancellationToken cancellationToken = default)
    {
        var topicArn = await GetOrCreateTopicArnAsync(topic, cancellationToken).ConfigureAwait(false);

        // Wrap body + headers into a single JSON envelope for SNS
        var envelope = new MessageEnvelope
        {
            Body = Convert.ToBase64String(message.Body.Span),
            Headers = message.Headers is not null ? new Dictionary<string, string>(message.Headers) : null
        };

        var json = JsonSerializer.Serialize(envelope);

        await _sns.PublishAsync(new PublishRequest
        {
            TopicArn = topicArn,
            Message = json
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> SubscribeAsync(string topic, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        var setup = await EnsureSubscriptionSetupAsync(topic, cancellationToken).ConfigureAwait(false);

        // Start polling the SQS queue
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pollTask = Task.Run(async () =>
        {
            await PollQueueAsync(setup.QueueUrl, handler, cts.Token).ConfigureAwait(false);
        }, cts.Token);

        var handle = new SubscriptionHandle(
            setup.QueueUrl, setup.QueueName, setup.SubscriptionArn, setup.TopicArn, cts, pollTask,
            _sqs, _sns, _options, _logger);

        _activeSubscriptions.Add(handle);

        return handle;
    }

    /// <summary>
    /// Ensures per-node subscription infrastructure is created for a topic.
    /// Creates the SNS topic, a per-node SQS queue, sets the queue policy,
    /// and subscribes the queue to the topic. Results are cached so subsequent
    /// calls (including from <see cref="SubscribeAsync"/>) make no API calls.
    /// </summary>
    private async Task<SubscriptionSetup> EnsureSubscriptionSetupAsync(string topic, CancellationToken cancellationToken)
    {
        if (_subscriptionSetupCache.TryGetValue(topic, out var cached))
            return cached;

        var topicArn = await GetOrCreateTopicArnAsync(topic, cancellationToken).ConfigureAwait(false);

        // Create a per-node SQS queue
        var queueName = $"{_options.QueuePrefix}-{_hostId}";
        var createResponse = await _sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = queueName
        }, cancellationToken).ConfigureAwait(false);
        var queueUrl = createResponse.QueueUrl;

        // Get queue ARN for the subscription policy
        var queueAttrs = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = queueUrl,
            AttributeNames = ["QueueArn"]
        }, cancellationToken).ConfigureAwait(false);
        var queueArn = queueAttrs.QueueARN;

        // Set the SQS queue policy to allow SNS to send messages
        var policy = $$"""
        {
            "Version": "2012-10-17",
            "Statement": [{
                "Effect": "Allow",
                "Principal": {"Service": "sns.amazonaws.com"},
                "Action": "sqs:SendMessage",
                "Resource": "{{queueArn}}",
                "Condition": {
                    "ArnEquals": { "aws:SourceArn": "{{topicArn}}" }
                }
            }]
        }
        """;

        await _sqs.SetQueueAttributesAsync(new SetQueueAttributesRequest
        {
            QueueUrl = queueUrl,
            Attributes = new Dictionary<string, string>
            {
                ["Policy"] = policy
            }
        }, cancellationToken).ConfigureAwait(false);

        // Subscribe the SQS queue to the SNS topic
        var subscribeResponse = await _sns.SubscribeAsync(new SubscribeRequest
        {
            TopicArn = topicArn,
            Protocol = "sqs",
            Endpoint = queueArn,
            Attributes = new Dictionary<string, string>
            {
                // Enable raw message delivery so we get the message directly without SNS wrapper
                ["RawMessageDelivery"] = "true"
            }
        }, cancellationToken).ConfigureAwait(false);
        var subscriptionArn = subscribeResponse.SubscriptionArn;

        _logger.LogInformation(
            "Subscribed to SNS topic {TopicArn} via SQS queue {QueueName} (subscription={SubscriptionArn})",
            topicArn, queueName, subscriptionArn);

        var setup = new SubscriptionSetup(topicArn, queueName, queueUrl, subscriptionArn);
        _subscriptionSetupCache[topic] = setup;
        return setup;
    }

    private async Task PollQueueAsync(string queueUrl, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = _options.WaitTimeSeconds
                }, cancellationToken).ConfigureAwait(false);

                if (response.Messages is not { Count: > 0 })
                    continue;

                foreach (var sqsMessage in response.Messages)
                {
                    try
                    {
                        var envelope = JsonSerializer.Deserialize<MessageEnvelope>(sqsMessage.Body);
                        if (envelope is null)
                            continue;

                        var body = Convert.FromBase64String(envelope.Body);
                        var headers = envelope.Headers is not null
                            ? new Dictionary<string, string>(envelope.Headers)
                            : new Dictionary<string, string>();

                        var message = new PubSubMessage
                        {
                            Body = body,
                            Headers = headers
                        };

                        await handler(message, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing bus message from SQS queue");
                    }

                    // Delete processed message
                    try
                    {
                        await _sqs.DeleteMessageAsync(new DeleteMessageRequest
                        {
                            QueueUrl = queueUrl,
                            ReceiptHandle = sqsMessage.ReceiptHandle
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete processed message from SQS queue");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling SQS queue, retrying...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<string> GetOrCreateTopicArnAsync(string topic, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_options.TopicArn))
            return _options.TopicArn;

        if (_topicArnCache.TryGetValue(topic, out var cached))
            return cached;

        if (_options.AutoCreate)
        {
            var response = await _sns.CreateTopicAsync(new CreateTopicRequest
            {
                Name = topic
            }, cancellationToken).ConfigureAwait(false);

            _topicArnCache[topic] = response.TopicArn;
            return response.TopicArn;
        }

        // Find existing topic
        var findResponse = await _sns.FindTopicAsync(topic).ConfigureAwait(false);
        if (findResponse?.TopicArn is null)
            throw new InvalidOperationException($"SNS topic '{topic}' not found and AutoCreate is disabled.");

        _topicArnCache[topic] = findResponse.TopicArn;
        return findResponse.TopicArn;
    }

    /// <inheritdoc />
    public Task EnsureTopicsAsync(IReadOnlyList<string> topics, CancellationToken cancellationToken = default)
    {
        // Pre-create topics and per-node subscription infrastructure so
        // SubscribeAsync later makes zero API calls (all results are cached).
        return Task.WhenAll(topics.Select(topic => EnsureSubscriptionSetupAsync(topic, cancellationToken)));
    }

    private record SubscriptionSetup(string TopicArn, string QueueName, string QueueUrl, string SubscriptionArn);

    public async ValueTask DisposeAsync()
    {
        foreach (var handle in _activeSubscriptions)
            await handle.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class MessageEnvelope
    {
        public string Body { get; set; } = string.Empty;
        public Dictionary<string, string>? Headers { get; set; }
    }

    private sealed class SubscriptionHandle(
        string queueUrl,
        string queueName,
        string subscriptionArn,
        string topicArn,
        CancellationTokenSource cts,
        Task pollTask,
        IAmazonSQS sqs,
        IAmazonSimpleNotificationService sns,
        SnsSqsPubSubClientOptions options,
        ILogger logger) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // Stop polling
            await cts.CancelAsync().ConfigureAwait(false);
            try { await pollTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            cts.Dispose();

            if (!options.CleanupOnDispose)
                return;

            // Unsubscribe from SNS
            try
            {
                await sns.UnsubscribeAsync(new UnsubscribeRequest
                {
                    SubscriptionArn = subscriptionArn
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to unsubscribe {SubscriptionArn} from SNS topic {TopicArn}",
                    subscriptionArn, topicArn);
            }

            // Delete per-node SQS queue
            try
            {
                await sqs.DeleteQueueAsync(new DeleteQueueRequest
                {
                    QueueUrl = queueUrl
                }).ConfigureAwait(false);

                logger.LogInformation("Deleted per-node SQS queue {QueueName}", queueName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete per-node SQS queue {QueueName}", queueName);
            }
        }
    }
}
