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
public sealed class SqsPubSubClient : IPubSubClient, IAsyncDisposable
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly IAmazonSQS _sqs;
    private readonly SqsPubSubClientOptions _options;
    private readonly string _hostId;
    private readonly string? _resourcePrefix;
    private readonly ILogger<SqsPubSubClient> _logger;
    private readonly ConcurrentDictionary<string, string> _topicArnCache = new();
    private readonly ConcurrentDictionary<string, SubscriptionSetup> _subscriptionSetupCache = new();
    private readonly ConcurrentBag<SubscriptionHandle> _activeSubscriptions = [];
    private readonly SemaphoreSlim _queueSetupLock = new(1, 1);
    private (string QueueName, string QueueUrl, string QueueArn)? _sharedQueue;

    public SqsPubSubClient(
        IAmazonSimpleNotificationService sns,
        IAmazonSQS sqs,
        SqsPubSubClientOptions options,
        DistributedNotificationOptions notificationOptions,
        ILogger<SqsPubSubClient> logger)
    {
        _sns = sns;
        _sqs = sqs;
        _options = options;
        _hostId = notificationOptions.HostId;
        _resourcePrefix = notificationOptions.ResourcePrefix;
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
            setup.SubscriptionArn, setup.TopicArn, cts, pollTask,
            _sns, _options, _logger);

        _activeSubscriptions.Add(handle);

        return handle;
    }

    /// <summary>
    /// Ensures per-node subscription infrastructure is created for a topic.
    /// Ensures the shared per-node SQS queue exists (created once, cached).
    /// </summary>
    private async Task<(string QueueName, string QueueUrl, string QueueArn)> EnsureSharedQueueAsync(CancellationToken cancellationToken)
    {
        if (_sharedQueue is not null)
            return _sharedQueue.Value;

        await _queueSetupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sharedQueue is not null)
                return _sharedQueue.Value;

            var queuePrefix = string.IsNullOrEmpty(_resourcePrefix)
                ? _options.QueuePrefix
                : $"{_resourcePrefix}-{_options.QueuePrefix}";
            var queueName = $"{queuePrefix}-{_hostId}";
            var stepSw = System.Diagnostics.Stopwatch.StartNew();

            var createResponse = await _sqs.CreateQueueAsync(new CreateQueueRequest
            {
                QueueName = queueName
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("EnsureSharedQueue: CreateQueue completed in {ElapsedMs}ms", stepSw.ElapsedMilliseconds);
            stepSw.Restart();

            // Get the queue ARN — needed for the SNS subscription policy
            var queueAttrs = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = createResponse.QueueUrl,
                AttributeNames = ["QueueArn"]
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("EnsureSharedQueue: GetQueueAttributes completed in {ElapsedMs}ms", stepSw.ElapsedMilliseconds);

            _sharedQueue = (queueName, createResponse.QueueUrl, queueAttrs.QueueARN);
            return _sharedQueue.Value;
        }
        finally
        {
            _queueSetupLock.Release();
        }
    }

    /// <summary>
    /// Ensures per-node subscription infrastructure is created for a topic.
    /// Creates the SNS topic, reuses the shared per-node SQS queue, sets the queue policy,
    /// and subscribes the queue to the topic. Results are cached so subsequent
    /// calls (including from <see cref="SubscribeAsync"/>) make no API calls.
    /// </summary>
    private async Task<SubscriptionSetup> EnsureSubscriptionSetupAsync(string topic, CancellationToken cancellationToken)
    {
        if (_subscriptionSetupCache.TryGetValue(topic, out var cached))
            return cached;

        var topicArn = await GetOrCreateTopicArnAsync(topic, cancellationToken).ConfigureAwait(false);
        var queue = await EnsureSharedQueueAsync(cancellationToken).ConfigureAwait(false);

        // Subscribe the SQS queue to the SNS topic
        var subscribeResponse = await _sns.SubscribeAsync(new SubscribeRequest
        {
            TopicArn = topicArn,
            Protocol = "sqs",
            Endpoint = queue.QueueArn,
            Attributes = new Dictionary<string, string>
            {
                // Enable raw message delivery so we get the message directly without SNS wrapper
                ["RawMessageDelivery"] = "true"
            }
        }, cancellationToken).ConfigureAwait(false);
        var subscriptionArn = subscribeResponse.SubscriptionArn;

        _logger.LogInformation(
            "Subscribed to SNS topic {TopicArn} via SQS queue {QueueName} (subscription={SubscriptionArn})",
            topicArn, queue.QueueName, subscriptionArn);

        var setup = new SubscriptionSetup(topicArn, queue.QueueName, queue.QueueUrl, subscriptionArn);
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

        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (_options.AutoCreate)
        {
            var response = await _sns.CreateTopicAsync(new CreateTopicRequest
            {
                Name = topic
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("CreateTopic {Topic} completed in {ElapsedMs}ms", topic, sw.ElapsedMilliseconds);

            _topicArnCache[topic] = response.TopicArn;
            return response.TopicArn;
        }

        // Find existing topic
        var findResponse = await _sns.FindTopicAsync(topic).ConfigureAwait(false);
        _logger.LogDebug("FindTopic {Topic} completed in {ElapsedMs}ms", topic, sw.ElapsedMilliseconds);

        if (findResponse?.TopicArn is null)
            throw new InvalidOperationException($"SNS topic '{topic}' not found and AutoCreate is disabled.");

        _topicArnCache[topic] = findResponse.TopicArn;
        return findResponse.TopicArn;
    }

    /// <inheritdoc />
    public async Task EnsureTopicsAsync(IReadOnlyList<string> topics, CancellationToken cancellationToken = default)
    {
        if (topics.Count == 0)
            return;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Create the shared per-node SQS queue and all SNS topics in parallel
        var queueTask = EnsureSharedQueueAsync(cancellationToken);
        var topicTasks = topics.Select(t => GetOrCreateTopicArnAsync(t, cancellationToken)).ToArray();

        await Task.WhenAll(topicTasks).ConfigureAwait(false);
        var queue = await queueTask.ConfigureAwait(false);

        _logger.LogInformation("EnsureTopics: queue + topics created in {ElapsedMs}ms", sw.ElapsedMilliseconds);

        var topicArns = topicTasks.Select(t => t.Result).ToList();

        // 2. Set a single SQS policy allowing ALL SNS topics to send messages
        var arnList = string.Join("\", \"", topicArns);
        var policy = $$"""
        {
            "Version": "2012-10-17",
            "Statement": [{
                "Effect": "Allow",
                "Principal": {"Service": "sns.amazonaws.com"},
                "Action": "sqs:SendMessage",
                "Resource": "{{queue.QueueArn}}",
                "Condition": {
                    "ArnEquals": { "aws:SourceArn": ["{{arnList}}"] }
                }
            }]
        }
        """;

        await _sqs.SetQueueAttributesAsync(new SetQueueAttributesRequest
        {
            QueueUrl = queue.QueueUrl,
            Attributes = new Dictionary<string, string>
            {
                ["Policy"] = policy
            }
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("EnsureTopics: policy set in {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // 3. Subscribe the queue to all topics in parallel
        await Task.WhenAll(topics.Select(topic => EnsureSubscriptionSetupAsync(topic, cancellationToken))).ConfigureAwait(false);

        _logger.LogInformation("EnsureTopics: complete in {ElapsedMs}ms ({Count} topics)", sw.ElapsedMilliseconds, topics.Count);
    }

    private record SubscriptionSetup(string TopicArn, string QueueName, string QueueUrl, string SubscriptionArn);

    public async ValueTask DisposeAsync()
    {
        foreach (var handle in _activeSubscriptions)
            await handle.DisposeAsync().ConfigureAwait(false);

        // Clean up the shared per-node SQS queue if configured.
        // Individual SubscriptionHandle disposals unsubscribe from SNS, but the
        // shared queue is owned by this client and must be deleted here.
        if (_sharedQueue is not null && _options.CleanupOnDispose)
        {
            try
            {
                await _sqs.DeleteQueueAsync(new DeleteQueueRequest
                {
                    QueueUrl = _sharedQueue.Value.QueueUrl
                }).ConfigureAwait(false);

                _logger.LogInformation("Deleted shared per-node SQS queue {QueueName}", _sharedQueue.Value.QueueName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete shared per-node SQS queue {QueueName}", _sharedQueue.Value.QueueName);
            }
        }
    }

    private sealed class MessageEnvelope
    {
        public string Body { get; set; } = string.Empty;
        public Dictionary<string, string>? Headers { get; set; }
    }

    private sealed class SubscriptionHandle(
        string subscriptionArn,
        string topicArn,
        CancellationTokenSource cts,
        Task pollTask,
        IAmazonSimpleNotificationService sns,
        SqsPubSubClientOptions options,
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

            // Shared per-node SQS queue is cleaned up by SqsPubSubClient.DisposeAsync()
        }
    }
}
