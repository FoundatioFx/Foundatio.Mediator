using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// <see cref="IPubSubClient"/> implementation using SNS for fan-out publishing and a per-node SQS
/// queue for delivery. Headers travel as SNS/SQS message attributes (raw delivery) and the body as
/// UTF-8 text. Each node tags its queue with a heartbeat so the queues of crashed nodes can be
/// swept by whichever node starts next.
/// </summary>
public sealed class SqsPubSubClient : IPubSubClient
{
    private const int MaxBatchEntries = 10;
    internal const string RoleTag = "fm:role";
    internal const string HostTag = "fm:host";
    internal const string HeartbeatTag = "fm:heartbeat";
    internal const string SubscriptionRole = "notification-subscription";

    private readonly IAmazonSimpleNotificationService _sns;
    private readonly IAmazonSQS _sqs;
    private readonly SqsPubSubClientOptions _options;
    private readonly string _hostId;
    private readonly string _queuePrefix;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqsPubSubClient> _logger;
    private readonly ConcurrentDictionary<string, string> _topicArnCache = new();
    private readonly ConcurrentDictionary<string, Task<SubscriptionSetup>> _subscriptionSetups = new();
    private readonly ConcurrentBag<SubscriptionHandle> _activeSubscriptions = [];
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly SemaphoreSlim _policyLock = new(1, 1);
    private readonly Lock _sweepSync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private volatile SharedQueue? _sharedQueue;
    private Task? _heartbeatTask;
    private Task? _sweepTask;
    private string? _appliedPolicyKey;
    private int _disposed;

    public SqsPubSubClient(
        IAmazonSimpleNotificationService sns,
        IAmazonSQS sqs,
        SqsPubSubClientOptions options,
        DistributedNotificationOptions notificationOptions,
        ILogger<SqsPubSubClient> logger,
        TimeProvider? timeProvider = null)
    {
        _sns = sns;
        _sqs = sqs;
        _options = options;
        _hostId = notificationOptions.HostId;
        _queuePrefix = string.IsNullOrEmpty(notificationOptions.ResourcePrefix)
            ? options.QueuePrefix
            : $"{notificationOptions.ResourcePrefix}-{options.QueuePrefix}";
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task PublishAsync(string topic, IReadOnlyList<PubSubEntry> messages, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            return;

        var topicArn = await GetOrCreateTopicArnAsync(topic, cancellationToken).ConfigureAwait(false);

        var batch = new List<PublishBatchRequestEntry>(MaxBatchEntries);
        int batchBytes = 0;

        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            var body = SqsPayload.EncodeBody(message.Body);
            int size = SqsPayload.Validate("topic", topic, body, message.Headers);

            if (batch.Count == MaxBatchEntries || (batch.Count > 0 && batchBytes + size > SqsPayload.MaxMessageBytes))
            {
                await PublishBatchAsync(topicArn, topic, batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
                batchBytes = 0;
            }

            var entry = new PublishBatchRequestEntry
            {
                Id = i.ToString(CultureInfo.InvariantCulture),
                Message = body
            };

            if (message.Headers is { Count: > 0 })
            {
                entry.MessageAttributes = new Dictionary<string, Amazon.SimpleNotificationService.Model.MessageAttributeValue>(message.Headers.Count);
                foreach (var (key, value) in message.Headers)
                    entry.MessageAttributes[key] = new Amazon.SimpleNotificationService.Model.MessageAttributeValue { DataType = "String", StringValue = value };
            }

            batch.Add(entry);
            batchBytes += size;
        }

        if (batch.Count > 0)
            await PublishBatchAsync(topicArn, topic, batch, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishBatchAsync(string topicArn, string topic, List<PublishBatchRequestEntry> entries, CancellationToken cancellationToken)
    {
        var response = await _sns.PublishBatchAsync(new PublishBatchRequest
        {
            TopicArn = topicArn,
            PublishBatchRequestEntries = [.. entries]
        }, cancellationToken).ConfigureAwait(false);

        if (response.Failed is { Count: > 0 })
        {
            var first = response.Failed[0];
            throw new InvalidOperationException(
                $"Failed to publish {response.Failed.Count} message(s) to SNS topic '{topic}': [{first.Code}] {first.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> SubscribeAsync(string topic, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var setup = await EnsureSubscriptionSetupAsync(topic, cancellationToken).ConfigureAwait(false);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var pollTask = PollQueueAsync(setup.Queue, handler, cts.Token);

        var handle = new SubscriptionHandle(setup, cts, pollTask, this);
        _activeSubscriptions.Add(handle);
        return handle;
    }

    /// <inheritdoc />
    public async Task EnsureTopicsAsync(IReadOnlyList<TopicDefinition> topics, CancellationToken cancellationToken = default)
    {
        if (topics.Count == 0)
            return;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var queueTask = EnsureSharedQueueAsync(cancellationToken);
        await Task.WhenAll(topics.Select(t => GetOrCreateTopicArnAsync(t.Name, cancellationToken))).ConfigureAwait(false);
        await queueTask.ConfigureAwait(false);

        await Task.WhenAll(topics.Select(t => EnsureSubscriptionSetupAsync(t.Name, cancellationToken))).ConfigureAwait(false);

        _logger.LogInformation("EnsureTopics: {Count} topic(s) subscribed in {ElapsedMs}ms", topics.Count, sw.ElapsedMilliseconds);
    }

    private async Task<SubscriptionSetup> EnsureSubscriptionSetupAsync(string topic, CancellationToken cancellationToken)
    {
        var task = _subscriptionSetups.GetOrAdd(topic, t => SetupSubscriptionAsync(t, cancellationToken));
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            _subscriptionSetups.TryRemove(new KeyValuePair<string, Task<SubscriptionSetup>>(topic, task));
            throw;
        }
    }

    private async Task<SubscriptionSetup> SetupSubscriptionAsync(string topic, CancellationToken cancellationToken)
    {
        var topicArn = await GetOrCreateTopicArnAsync(topic, cancellationToken).ConfigureAwait(false);
        var queue = await EnsureSharedQueueAsync(cancellationToken).ConfigureAwait(false);
        var sweep = SweepOnceAsync(cancellationToken);

        await ApplyQueuePolicyAsync(queue, cancellationToken).ConfigureAwait(false);

        var subscribeResponse = await _sns.SubscribeAsync(new SubscribeRequest
        {
            TopicArn = topicArn,
            Protocol = "sqs",
            Endpoint = queue.Arn,
            Attributes = new Dictionary<string, string> { ["RawMessageDelivery"] = "true" }
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Subscribed SQS queue {QueueName} to SNS topic {TopicArn} (subscription={SubscriptionArn})",
            queue.Name, topicArn, subscribeResponse.SubscriptionArn);

        await sweep.ConfigureAwait(false);
        return new SubscriptionSetup(topic, topicArn, queue, subscribeResponse.SubscriptionArn);
    }

    private async Task<SharedQueue> EnsureSharedQueueAsync(CancellationToken cancellationToken)
    {
        if (_sharedQueue is { } existing)
            return existing;

        await _queueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sharedQueue is { } created)
                return created;

            var queueName = $"{_queuePrefix}-{_hostId}";
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var queueUrl = await CreateSubscriptionQueueAsync(queueName, cancellationToken).ConfigureAwait(false);
            var attributes = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = [QueueAttributeName.QueueArn]
            }, cancellationToken).ConfigureAwait(false);

            var queue = new SharedQueue(queueName, queueUrl, attributes.QueueARN);
            _sharedQueue = queue;
            _heartbeatTask = HeartbeatLoopAsync(queue, _lifetime.Token);

            _logger.LogDebug("Subscription queue {QueueName} ready in {ElapsedMs}ms", queueName, sw.ElapsedMilliseconds);
            return queue;
        }
        finally
        {
            _queueLock.Release();
        }
    }

    private async Task<string> CreateSubscriptionQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        var request = new CreateQueueRequest
        {
            QueueName = queueName,
            Attributes = new Dictionary<string, string>
            {
                [QueueAttributeName.MessageRetentionPeriod] = ((int)Math.Ceiling(_options.SubscriptionQueueRetention.TotalSeconds)).ToString(CultureInfo.InvariantCulture)
            },
            Tags = new Dictionary<string, string>
            {
                [RoleTag] = SubscriptionRole,
                [HostTag] = _hostId,
                [HeartbeatTag] = _timeProvider.GetUtcNow().ToString("O")
            }
        };

        try
        {
            var response = await _sqs.CreateQueueAsync(request, cancellationToken).ConfigureAwait(false);
            return response.QueueUrl;
        }
        catch (QueueNameExistsException)
        {
            // Left behind by a previous instance of this host id with different settings; adopt and re-tag it.
            var existing = await _sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = queueName }, cancellationToken).ConfigureAwait(false);
            await _sqs.SetQueueAttributesAsync(new SetQueueAttributesRequest { QueueUrl = existing.QueueUrl, Attributes = request.Attributes }, cancellationToken).ConfigureAwait(false);
            await _sqs.TagQueueAsync(new TagQueueRequest { QueueUrl = existing.QueueUrl, Tags = request.Tags }, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Adopted existing subscription queue {QueueName}", queueName);
            return existing.QueueUrl;
        }
        catch (QueueDeletedRecentlyException)
        {
            // SQS refuses to recreate a name for 60 seconds after deletion; a stable host id that restarts quickly hits this.
            request.QueueName = $"{queueName}-{_timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}";
            _logger.LogWarning("Subscription queue {QueueName} was deleted less than 60 seconds ago; using {FallbackQueueName} instead",
                queueName, request.QueueName);
            var response = await _sqs.CreateQueueAsync(request, cancellationToken).ConfigureAwait(false);
            return response.QueueUrl;
        }
    }

    private async Task HeartbeatLoopAsync(SharedQueue queue, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HeartbeatInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
                await _sqs.TagQueueAsync(new TagQueueRequest
                {
                    QueueUrl = queue.Url,
                    Tags = new Dictionary<string, string> { [HeartbeatTag] = _timeProvider.GetUtcNow().ToString("O") }
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh the heartbeat tag on subscription queue {QueueName}", queue.Name);
            }
        }
    }

    private async Task ApplyQueuePolicyAsync(SharedQueue queue, CancellationToken cancellationToken)
    {
        await _policyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var topicArns = _topicArnCache.Values.Distinct().Order(StringComparer.Ordinal).ToArray();
            var key = string.Join("|", topicArns);
            if (key == _appliedPolicyKey)
                return;

            var policy = JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new[]
                {
                    new
                    {
                        Effect = "Allow",
                        Principal = new { Service = "sns.amazonaws.com" },
                        Action = "sqs:SendMessage",
                        Resource = queue.Arn,
                        Condition = new { ArnEquals = new Dictionary<string, string[]> { ["aws:SourceArn"] = topicArns } }
                    }
                }
            });

            await _sqs.SetQueueAttributesAsync(new SetQueueAttributesRequest
            {
                QueueUrl = queue.Url,
                Attributes = new Dictionary<string, string> { [QueueAttributeName.Policy] = policy }
            }, cancellationToken).ConfigureAwait(false);

            _appliedPolicyKey = key;
            _logger.LogDebug("Queue policy on {QueueName} allows {Count} topic(s)", queue.Name, topicArns.Length);
        }
        finally
        {
            _policyLock.Release();
        }
    }

    private Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        lock (_sweepSync)
            return _sweepTask ??= SweepStaleSubscriptionQueuesAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes subscription queues (and their SNS subscriptions) whose owner stopped heart-beating. Never throws.
    /// </summary>
    private async Task SweepStaleSubscriptionQueuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ownUrl = _sharedQueue?.Url;
            var cutoff = _timeProvider.GetUtcNow() - _options.StaleSubscriptionAge;
            int removed = 0;
            string? nextToken = null;

            do
            {
                var page = await _sqs.ListQueuesAsync(new ListQueuesRequest
                {
                    QueueNamePrefix = _queuePrefix + "-",
                    MaxResults = 1000,
                    NextToken = nextToken
                }, cancellationToken).ConfigureAwait(false);

                nextToken = page.NextToken;
                foreach (var queueUrl in page.QueueUrls ?? [])
                {
                    if (queueUrl == ownUrl)
                        continue;

                    if (await TryRemoveStaleQueueAsync(queueUrl, cutoff, cancellationToken).ConfigureAwait(false))
                        removed++;
                }
            } while (!string.IsNullOrEmpty(nextToken));

            if (removed > 0)
                _logger.LogInformation("Removed {Count} stale notification subscription queue(s)", removed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sweep of stale notification subscription queues failed");
        }
    }

    private async Task<bool> TryRemoveStaleQueueAsync(string queueUrl, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        try
        {
            var tags = (await _sqs.ListQueueTagsAsync(new ListQueueTagsRequest { QueueUrl = queueUrl }, cancellationToken).ConfigureAwait(false)).Tags;
            if (tags is null || !tags.TryGetValue(RoleTag, out var role) || role != SubscriptionRole)
                return false;

            if (tags.TryGetValue(HeartbeatTag, out var heartbeat)
                && DateTimeOffset.TryParse(heartbeat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastSeen)
                && lastSeen >= cutoff)
                return false;

            var queueArn = (await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = [QueueAttributeName.QueueArn]
            }, cancellationToken).ConfigureAwait(false)).QueueARN;

            foreach (var topicArn in _topicArnCache.Values.Distinct())
                await UnsubscribeQueueAsync(topicArn, queueArn, cancellationToken).ConfigureAwait(false);

            await _sqs.DeleteQueueAsync(new DeleteQueueRequest { QueueUrl = queueUrl }, cancellationToken).ConfigureAwait(false);

            tags.TryGetValue(HostTag, out var host);
            _logger.LogInformation("Deleted stale notification subscription queue {QueueUrl} of host {HostId} (last heartbeat {Heartbeat})",
                queueUrl, host, tags.GetValueOrDefault(HeartbeatTag));
            return true;
        }
        catch (QueueDoesNotExistException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to remove stale notification subscription queue {QueueUrl}", queueUrl);
            return false;
        }
    }

    private async Task UnsubscribeQueueAsync(string topicArn, string queueArn, CancellationToken cancellationToken)
    {
        string? nextToken = null;
        do
        {
            var page = await _sns.ListSubscriptionsByTopicAsync(new ListSubscriptionsByTopicRequest
            {
                TopicArn = topicArn,
                NextToken = nextToken
            }, cancellationToken).ConfigureAwait(false);

            nextToken = page.NextToken;
            foreach (var subscription in page.Subscriptions ?? [])
            {
                // Unconfirmed subscriptions report "PendingConfirmation" instead of an ARN.
                if (subscription.Endpoint != queueArn || subscription.SubscriptionArn?.StartsWith("arn:", StringComparison.Ordinal) != true)
                    continue;

                await _sns.UnsubscribeAsync(new UnsubscribeRequest { SubscriptionArn = subscription.SubscriptionArn }, cancellationToken).ConfigureAwait(false);
            }
        } while (!string.IsNullOrEmpty(nextToken));
    }

    private async Task PollQueueAsync(SharedQueue queue, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queue.Url,
                    MaxNumberOfMessages = MaxBatchEntries,
                    WaitTimeSeconds = _options.WaitTimeSeconds,
                    MessageAttributeNames = ["All"]
                }, cancellationToken).ConfigureAwait(false);

                if (response.Messages is not { Count: > 0 })
                    continue;

                // Pub/sub is at-most-once: acknowledge the batch before any handler runs.
                await DeleteBatchAsync(queue, response.Messages, cancellationToken).ConfigureAwait(false);

                await Task.WhenAll(response.Messages.Select(m => InvokeHandlerAsync(queue, m, handler, cancellationToken))).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling subscription queue {QueueName}, retrying", queue.Name);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task DeleteBatchAsync(SharedQueue queue, List<Message> messages, CancellationToken cancellationToken)
    {
        var response = await _sqs.DeleteMessageBatchAsync(new DeleteMessageBatchRequest
        {
            QueueUrl = queue.Url,
            Entries = messages.Select((m, i) => new DeleteMessageBatchRequestEntry
            {
                Id = i.ToString(CultureInfo.InvariantCulture),
                ReceiptHandle = m.ReceiptHandle
            }).ToList()
        }, cancellationToken).ConfigureAwait(false);

        if (response.Failed is { Count: > 0 })
            _logger.LogWarning("Failed to delete {Count} notification(s) from {QueueName}; they may be delivered again: [{Code}] {Message}",
                response.Failed.Count, queue.Name, response.Failed[0].Code, response.Failed[0].Message);
    }

    private async Task InvokeHandlerAsync(SharedQueue queue, Message sqsMessage, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        try
        {
            var headers = new Dictionary<string, string>();
            if (sqsMessage.MessageAttributes is { Count: > 0 })
            {
                foreach (var (key, attribute) in sqsMessage.MessageAttributes)
                    headers[key] = attribute.StringValue;
            }

            await handler(new PubSubMessage
            {
                Body = SqsPayload.DecodeBody(sqsMessage.Body),
                Headers = headers
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling notification {MessageId} from {QueueName}", sqsMessage.MessageId, queue.Name);
        }
    }

    private async Task<string> GetOrCreateTopicArnAsync(string topic, CancellationToken cancellationToken)
    {
        if (_topicArnCache.TryGetValue(topic, out var cached))
            return cached;

        if (!string.IsNullOrEmpty(_options.TopicArn))
        {
            _topicArnCache[topic] = _options.TopicArn;
            return _options.TopicArn;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (_options.AutoCreate)
        {
            var response = await _sns.CreateTopicAsync(new CreateTopicRequest { Name = topic }, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("CreateTopic {Topic} completed in {ElapsedMs}ms", topic, sw.ElapsedMilliseconds);
            _topicArnCache[topic] = response.TopicArn;
            return response.TopicArn;
        }

        var found = await _sns.FindTopicAsync(topic).ConfigureAwait(false);
        _logger.LogDebug("FindTopic {Topic} completed in {ElapsedMs}ms", topic, sw.ElapsedMilliseconds);

        if (found?.TopicArn is null)
            throw new InvalidOperationException($"SNS topic '{topic}' not found and {nameof(SqsPubSubClientOptions.AutoCreate)} is disabled.");

        _topicArnCache[topic] = found.TopicArn;
        return found.TopicArn;
    }

    private void OnSubscriptionDisposed(SubscriptionSetup setup)
    {
        if (_subscriptionSetups.TryGetValue(setup.Topic, out var task) && task.IsCompletedSuccessfully && ReferenceEquals(task.Result, setup))
            _subscriptionSetups.TryRemove(new KeyValuePair<string, Task<SubscriptionSetup>>(setup.Topic, task));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifetime.CancelAsync().ConfigureAwait(false);

        foreach (var handle in _activeSubscriptions)
            await handle.DisposeAsync().ConfigureAwait(false);

        if (_heartbeatTask is { } heartbeat)
        {
            try { await heartbeat.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        if (_sweepTask is { } sweep)
            await sweep.ConfigureAwait(false);

        if (_sharedQueue is { } queue && _options.CleanupOnDispose)
        {
            try
            {
                await _sqs.DeleteQueueAsync(new DeleteQueueRequest { QueueUrl = queue.Url }).ConfigureAwait(false);
                _logger.LogInformation("Deleted subscription queue {QueueName}", queue.Name);
            }
            catch (QueueDoesNotExistException)
            {
                _logger.LogDebug("Subscription queue {QueueName} was already deleted", queue.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete subscription queue {QueueName}", queue.Name);
            }
        }

        _lifetime.Dispose();
    }

    private sealed record SharedQueue(string Name, string Url, string Arn);

    private sealed record SubscriptionSetup(string Topic, string TopicArn, SharedQueue Queue, string SubscriptionArn);

    private sealed class SubscriptionHandle(
        SubscriptionSetup setup,
        CancellationTokenSource cts,
        Task pollTask,
        SqsPubSubClient owner) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await cts.CancelAsync().ConfigureAwait(false);
            try { await pollTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            cts.Dispose();

            if (!owner._options.CleanupOnDispose)
                return;

            owner.OnSubscriptionDisposed(setup);

            try
            {
                await owner._sns.UnsubscribeAsync(new UnsubscribeRequest { SubscriptionArn = setup.SubscriptionArn }).ConfigureAwait(false);
            }
            catch (NotFoundException)
            {
                owner._logger.LogDebug("Subscription {SubscriptionArn} was already removed", setup.SubscriptionArn);
            }
            catch (Exception ex)
            {
                owner._logger.LogWarning(ex, "Failed to unsubscribe {SubscriptionArn} from SNS topic {TopicArn}", setup.SubscriptionArn, setup.TopicArn);
            }
        }
    }
}
