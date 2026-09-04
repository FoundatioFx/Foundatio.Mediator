using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// <see cref="IQueueClient"/> implementation backed by Amazon SQS. Headers map to SQS message
/// attributes and the body travels as UTF-8 text in the SQS message body.
/// </summary>
public sealed class SqsQueueClient : IQueueClient
{
    private const int MaxBatchEntries = 10;
    private const int MaxRedriveReceiveCount = 1000;
    private static readonly TimeSpan s_maxVisibilityTimeout = TimeSpan.FromHours(12);
    private static readonly TimeSpan s_minRetention = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan s_maxRetention = TimeSpan.FromDays(14);
    private static readonly TimeSpan s_dlqNotFoundTtl = TimeSpan.FromMinutes(1);
    private static readonly string s_deadLetterSuffix = QueueDefinition.DeadLetterQueueNameFor(string.Empty);

    private readonly IAmazonSQS _sqs;
    private readonly SqsQueueClientOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqsQueueClient> _logger;
    private readonly ConcurrentDictionary<string, string> _queueUrlCache = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _dlqNotFound = new();

    public SqsQueueClient(IAmazonSQS sqs, SqsQueueClientOptions? options = null, TimeProvider? timeProvider = null, ILogger<SqsQueueClient>? logger = null)
    {
        _sqs = sqs;
        _options = options ?? new SqsQueueClientOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SqsQueueClient>.Instance;
    }

    public async Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return;

        var queueUrl = await GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);

        var batch = new List<SendMessageBatchRequestEntry>(MaxBatchEntries);
        int batchBytes = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var body = SqsPayload.EncodeBody(entry.Body);
            int size = SqsPayload.Validate("queue", queueName, body, entry.Headers);

            // A batch is limited to 10 entries and to the single-message byte limit in total.
            if (batch.Count == MaxBatchEntries || (batch.Count > 0 && batchBytes + size > SqsPayload.MaxMessageBytes))
            {
                await SendBatchAsync(queueUrl, queueName, batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
                batchBytes = 0;
            }

            batch.Add(new SendMessageBatchRequestEntry
            {
                Id = i.ToString(CultureInfo.InvariantCulture),
                MessageBody = body,
                MessageAttributes = ToMessageAttributes(entry.Headers)
            });
            batchBytes += size;
        }

        if (batch.Count > 0)
            await SendBatchAsync(queueUrl, queueName, batch, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendBatchAsync(string queueUrl, string queueName, List<SendMessageBatchRequestEntry> entries, CancellationToken cancellationToken)
    {
        var response = await _sqs.SendMessageBatchAsync(new SendMessageBatchRequest
        {
            QueueUrl = queueUrl,
            Entries = [.. entries]
        }, cancellationToken).ConfigureAwait(false);

        if (response.Failed is { Count: > 0 })
        {
            var first = response.Failed[0];
            throw new InvalidOperationException(
                $"Failed to send {response.Failed.Count} message(s) to SQS queue '{queueName}': [{first.Code}] {first.Message}");
        }
    }

    /// <summary>
    /// Receives using the queue's configured visibility timeout.
    /// </summary>
    public Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, CancellationToken cancellationToken = default)
        => ReceiveAsync(queueName, maxCount, null, cancellationToken);

    public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken cancellationToken = default)
    {
        var queueUrl = await GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);

        var request = new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = Math.Min(maxCount, MaxBatchEntries),
            WaitTimeSeconds = _options.WaitTimeSeconds,
            MessageSystemAttributeNames = ["ApproximateReceiveCount", "SentTimestamp"],
            MessageAttributeNames = ["All"]
        };

        if (visibilityTimeout is { } vt && vt > TimeSpan.Zero)
            request.VisibilityTimeout = ToSeconds(vt);

        var response = await _sqs.ReceiveMessageAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Messages is not { Count: > 0 })
            return [];

        var now = _timeProvider.GetUtcNow();
        var results = new List<QueueMessage>(response.Messages.Count);

        foreach (var sqsMessage in response.Messages)
        {
            var headers = new Dictionary<string, string>();
            if (sqsMessage.MessageAttributes is { Count: > 0 })
            {
                foreach (var (key, attr) in sqsMessage.MessageAttributes)
                    headers[key] = attr.StringValue;
            }

            int dequeueCount = 1;
            if (sqsMessage.Attributes?.TryGetValue("ApproximateReceiveCount", out var receiveCountStr) == true
                && int.TryParse(receiveCountStr, out var parsed))
                dequeueCount = parsed;

            var enqueuedAt = now;
            if (sqsMessage.Attributes?.TryGetValue("SentTimestamp", out var sentTimestampStr) == true
                && long.TryParse(sentTimestampStr, out var epochMs))
                enqueuedAt = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);

            results.Add(new QueueMessage
            {
                Id = sqsMessage.MessageId,
                Body = SqsPayload.DecodeBody(sqsMessage.Body),
                Headers = headers,
                QueueName = queueName,
                DequeueCount = dequeueCount,
                EnqueuedAt = enqueuedAt,
                DequeuedAt = now,
                NativeMessage = sqsMessage
            });
        }

        return results;
    }

    public async Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default)
    {
        var queueUrl = await GetQueueUrlAsync(message.QueueName, cancellationToken).ConfigureAwait(false);
        var sqsMessage = GetNativeMessage(message);

        await _sqs.DeleteMessageAsync(new DeleteMessageRequest
        {
            QueueUrl = queueUrl,
            ReceiptHandle = sqsMessage.ReceiptHandle
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default)
    {
        var queueUrl = await GetQueueUrlAsync(message.QueueName, cancellationToken).ConfigureAwait(false);
        var sqsMessage = GetNativeMessage(message);

        await _sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
        {
            QueueUrl = queueUrl,
            ReceiptHandle = sqsMessage.ReceiptHandle,
            VisibilityTimeout = Math.Max(0, ToSeconds(delay))
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default)
    {
        var dlqName = QueueDefinition.DeadLetterQueueNameFor(message.QueueName);

        var headers = new Dictionary<string, string>(message.Headers)
        {
            [MessageHeaders.DeadLetterReason] = reason,
            [MessageHeaders.DeadLetteredAt] = _timeProvider.GetUtcNow().ToString("O"),
            [MessageHeaders.OriginalQueueName] = message.QueueName,
            [MessageHeaders.DeadLetterDequeueCount] = message.DequeueCount.ToString(CultureInfo.InvariantCulture)
        };

        await SendAsync(dlqName, [new QueueEntry { Body = message.Body, Headers = headers }], cancellationToken).ConfigureAwait(false);
        _dlqNotFound.TryRemove(dlqName, out _);
        await CompleteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default)
    {
        var queueUrl = await GetQueueUrlAsync(message.QueueName, cancellationToken).ConfigureAwait(false);
        var sqsMessage = GetNativeMessage(message);

        try
        {
            await _sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
            {
                QueueUrl = queueUrl,
                ReceiptHandle = sqsMessage.ReceiptHandle,
                VisibilityTimeout = ToSeconds(extension)
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (ReceiptHandleIsInvalidException ex)
        {
            _logger.LogDebug(ex, "Receipt handle invalid for message {MessageId} on {QueueName}, message may have been completed or expired",
                message.Id, message.QueueName);
        }
        catch (MessageNotInflightException ex)
        {
            _logger.LogDebug(ex, "Message {MessageId} on {QueueName} is not in-flight, visibility timeout change skipped",
                message.Id, message.QueueName);
        }
        catch (AmazonSQSException ex) when (ex.Message.Contains("does not exist or is not available", StringComparison.OrdinalIgnoreCase))
        {
            // LocalStack reports an expired receipt handle as a generic AmazonSQSException.
            _logger.LogDebug(ex, "Receipt handle for message {MessageId} on {QueueName} is no longer valid, visibility timeout change skipped",
                message.Id, message.QueueName);
        }
    }

    /// <inheritdoc />
    public async Task EnsureQueuesAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        switch (_options.Provisioning)
        {
            case SqsProvisioningMode.Create:
                await ProvisionAsync(queues, cancellationToken).ConfigureAwait(false);
                break;
            case SqsProvisioningMode.Validate:
                await ValidateAsync(queues, cancellationToken).ConfigureAwait(false);
                break;
            default:
                return;
        }

        _logger.LogInformation("EnsureQueues ({Mode}): {Count} queue(s) ready in {ElapsedMs}ms", _options.Provisioning, queues.Count, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Creates every missing queue and dead-letter queue and updates the attributes of existing ones,
    /// regardless of <see cref="SqsQueueClientOptions.Provisioning"/>. Run this from a deployment step
    /// or one-off command with provisioning IAM rights when the application itself runs in
    /// <see cref="SqsProvisioningMode.Validate"/> or <see cref="SqsProvisioningMode.None"/>.
    /// </summary>
    public Task ProvisionAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken cancellationToken = default)
    {
        foreach (var definition in queues)
            ValidateDefinition(definition);

        return Task.WhenAll(queues.Select(q => ProvisionQueueAsync(q, cancellationToken)));
    }

    private async Task ProvisionQueueAsync(QueueDefinition definition, CancellationToken cancellationToken)
    {
        var attributes = MainQueueAttributes(definition);

        if (definition.DeadLetterEnabled)
        {
            var dlqUrl = await CreateOrUpdateQueueAsync(definition.DeadLetterQueueName, DeadLetterQueueAttributes(definition), cancellationToken).ConfigureAwait(false);
            var dlqArn = await GetQueueArnAsync(dlqUrl, cancellationToken).ConfigureAwait(false);
            attributes[QueueAttributeName.RedrivePolicy] = RedrivePolicyFor(dlqArn, definition.MaxAttempts);
        }

        await CreateOrUpdateQueueAsync(definition.Name, attributes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> CreateOrUpdateQueueAsync(string queueName, Dictionary<string, string>? attributes, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string queueUrl;

        try
        {
            var created = await _sqs.CreateQueueAsync(new CreateQueueRequest
            {
                QueueName = queueName,
                Attributes = attributes
            }, cancellationToken).ConfigureAwait(false);

            queueUrl = created.QueueUrl;
            _logger.LogDebug("CreateQueue {QueueName} completed in {ElapsedMs}ms", queueName, sw.ElapsedMilliseconds);
        }
        catch (QueueNameExistsException)
        {
            // SQS only reports this when the existing queue's attributes differ, so bring them in line.
            var existing = await _sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = queueName }, cancellationToken).ConfigureAwait(false);
            queueUrl = existing.QueueUrl;

            if (attributes is { Count: > 0 })
            {
                await _sqs.SetQueueAttributesAsync(new SetQueueAttributesRequest
                {
                    QueueUrl = queueUrl,
                    Attributes = attributes
                }, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Updated attributes of existing SQS queue {QueueName} in {ElapsedMs}ms", queueName, sw.ElapsedMilliseconds);
        }

        _queueUrlCache[queueName] = queueUrl;
        return queueUrl;
    }

    private async Task ValidateAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken cancellationToken)
    {
        foreach (var definition in queues)
            ValidateDefinition(definition);

        var problems = new ConcurrentBag<string>();
        await Task.WhenAll(queues.Select(q => ValidateQueueAsync(q, problems, cancellationToken))).ConfigureAwait(false);

        if (!problems.IsEmpty)
        {
            throw new InvalidOperationException(
                $"SQS queue validation failed with {problems.Count} problem(s); fix them or run {nameof(SqsQueueClient)}.{nameof(ProvisionAsync)} with provisioning rights:"
                + Environment.NewLine + string.Join(Environment.NewLine, problems.Order(StringComparer.Ordinal)));
        }
    }

    private async Task ValidateQueueAsync(QueueDefinition definition, ConcurrentBag<string> problems, CancellationToken cancellationToken)
    {
        string? dlqArn = null;
        if (definition.DeadLetterEnabled)
        {
            var dlqAttributes = await TryGetQueueAttributesAsync(definition.DeadLetterQueueName, cancellationToken).ConfigureAwait(false);
            if (dlqAttributes is null)
            {
                problems.Add($"Dead-letter queue '{definition.DeadLetterQueueName}' for queue '{definition.Name}' does not exist.");
            }
            else
            {
                dlqAttributes.TryGetValue(QueueAttributeName.QueueArn, out dlqArn);
                CompareAttributes(definition.DeadLetterQueueName, DeadLetterQueueAttributes(definition), dlqAttributes, problems);
            }
        }

        var actual = await TryGetQueueAttributesAsync(definition.Name, cancellationToken).ConfigureAwait(false);
        if (actual is null)
        {
            problems.Add($"Queue '{definition.Name}' does not exist.");
            return;
        }

        CompareAttributes(definition.Name, MainQueueAttributes(definition), actual, problems);

        if (!definition.DeadLetterEnabled)
            return;

        if (!actual.TryGetValue(QueueAttributeName.RedrivePolicy, out var redriveJson) || string.IsNullOrEmpty(redriveJson))
        {
            problems.Add($"Queue '{definition.Name}' has no RedrivePolicy; expected one targeting '{definition.DeadLetterQueueName}'.");
            return;
        }

        var (targetArn, maxReceiveCount) = ParseRedrivePolicy(redriveJson);
        if (dlqArn is not null && !string.Equals(targetArn, dlqArn, StringComparison.Ordinal))
            problems.Add($"Queue '{definition.Name}' RedrivePolicy targets '{targetArn}' but the dead-letter queue is '{dlqArn}'.");

        if (definition.MaxAttempts >= 0 && maxReceiveCount is { } count && count <= definition.MaxAttempts)
            problems.Add($"Queue '{definition.Name}' RedrivePolicy maxReceiveCount is {count} but must exceed MaxAttempts ({definition.MaxAttempts}) so the worker dead-letters first.");
    }

    private async Task<Dictionary<string, string>?> TryGetQueueAttributesAsync(string queueName, CancellationToken cancellationToken)
    {
        string queueUrl;
        try
        {
            var response = await _sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = queueName }, cancellationToken).ConfigureAwait(false);
            queueUrl = response.QueueUrl;
        }
        catch (QueueDoesNotExistException)
        {
            return null;
        }

        _queueUrlCache[queueName] = queueUrl;

        var attributes = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = queueUrl,
            AttributeNames = [QueueAttributeName.All]
        }, cancellationToken).ConfigureAwait(false);

        return attributes.Attributes ?? [];
    }

    private static void CompareAttributes(string queueName, Dictionary<string, string> expected, Dictionary<string, string> actual, ConcurrentBag<string> problems)
    {
        foreach (var (name, expectedValue) in expected)
        {
            actual.TryGetValue(name, out var actualValue);
            if (!AttributeValuesEqual(expectedValue, actualValue))
                problems.Add($"Queue '{queueName}' attribute {name} is '{actualValue ?? "(unset)"}' but the definition expects '{expectedValue}'.");
        }
    }

    private static bool AttributeValuesEqual(string expected, string? actual)
    {
        if (actual is null)
            return false;

        if (long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e)
            && long.TryParse(actual, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
            return e == a;

        return string.Equals(expected, actual, StringComparison.Ordinal);
    }

    private void ValidateDefinition(QueueDefinition definition)
    {
        if (definition.VisibilityTimeout <= TimeSpan.Zero || definition.VisibilityTimeout > s_maxVisibilityTimeout)
            throw new InvalidOperationException(
                $"Queue '{definition.Name}' declares a visibility timeout of {definition.VisibilityTimeout}, but SQS requires a value between 1 second and 12 hours.");

        if (definition.MessageRetention is { } retention && (retention < s_minRetention || retention > s_maxRetention))
            throw new InvalidOperationException(
                $"Queue '{definition.Name}' declares a message retention of {retention}, but SQS requires a value between 1 minute and 14 days.");

        if (definition.DeadLetterEnabled && (_options.DeadLetterRetention < s_minRetention || _options.DeadLetterRetention > s_maxRetention))
            throw new InvalidOperationException(
                $"{nameof(SqsQueueClientOptions)}.{nameof(SqsQueueClientOptions.DeadLetterRetention)} is {_options.DeadLetterRetention}, but SQS requires a value between 1 minute and 14 days.");
    }

    private static Dictionary<string, string> MainQueueAttributes(QueueDefinition definition)
    {
        var attributes = new Dictionary<string, string>
        {
            [QueueAttributeName.VisibilityTimeout] = ToSeconds(definition.VisibilityTimeout).ToString(CultureInfo.InvariantCulture)
        };

        if (definition.MessageRetention is { } retention)
            attributes[QueueAttributeName.MessageRetentionPeriod] = ToSeconds(retention).ToString(CultureInfo.InvariantCulture);

        return attributes;
    }

    private Dictionary<string, string> DeadLetterQueueAttributes(QueueDefinition definition) => new()
    {
        [QueueAttributeName.VisibilityTimeout] = ToSeconds(definition.VisibilityTimeout).ToString(CultureInfo.InvariantCulture),
        [QueueAttributeName.MessageRetentionPeriod] = ToSeconds(_options.DeadLetterRetention).ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// The native redrive threshold sits above the worker's <see cref="QueueDefinition.MaxAttempts"/> so the
    /// library's own dead-lettering (with reason headers) runs first; SQS only catches messages no worker ever acknowledged.
    /// </summary>
    private static string RedrivePolicyFor(string deadLetterArn, int maxAttempts)
    {
        int maxReceiveCount = maxAttempts < 0 ? MaxRedriveReceiveCount : Math.Min(maxAttempts + 2, MaxRedriveReceiveCount);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["deadLetterTargetArn"] = deadLetterArn,
            ["maxReceiveCount"] = maxReceiveCount.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static (string? TargetArn, int? MaxReceiveCount) ParseRedrivePolicy(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            string? arn = doc.RootElement.TryGetProperty("deadLetterTargetArn", out var arnElement) ? arnElement.GetString() : null;
            int? count = null;
            if (doc.RootElement.TryGetProperty("maxReceiveCount", out var countElement))
            {
                if (countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt32(out var n))
                    count = n;
                else if (countElement.ValueKind == JsonValueKind.String && int.TryParse(countElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                    count = s;
            }

            return (arn, count);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private async Task<string> GetQueueArnAsync(string queueUrl, CancellationToken cancellationToken)
    {
        var response = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = queueUrl,
            AttributeNames = [QueueAttributeName.QueueArn]
        }, cancellationToken).ConfigureAwait(false);

        return response.QueueARN;
    }

    private async Task<string> GetQueueUrlAsync(string queueName, CancellationToken cancellationToken)
    {
        if (_queueUrlCache.TryGetValue(queueName, out var cached))
            return cached;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await _sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = queueName }, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("GetQueueUrl {QueueName} completed in {ElapsedMs}ms", queueName, sw.ElapsedMilliseconds);
            _queueUrlCache[queueName] = response.QueueUrl;
            return response.QueueUrl;
        }
        catch (QueueDoesNotExistException) when (_options.Provisioning == SqsProvisioningMode.Create)
        {
            _logger.LogInformation("SQS queue {QueueName} does not exist; creating it on first use", queueName);

            // An undeclared dead-letter queue still gets the dead-letter retention; anything else keeps SQS defaults.
            Dictionary<string, string>? attributes = queueName.EndsWith(s_deadLetterSuffix, StringComparison.Ordinal)
                ? new() { [QueueAttributeName.MessageRetentionPeriod] = ToSeconds(_options.DeadLetterRetention).ToString(CultureInfo.InvariantCulture) }
                : null;

            return await CreateOrUpdateQueueAsync(queueName, attributes, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
    {
        var results = new List<QueueStats>(queueNames.Count);
        foreach (var queueName in queueNames)
        {
            var queueUrl = await GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);

            var response = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = [QueueAttributeName.ApproximateNumberOfMessages, QueueAttributeName.ApproximateNumberOfMessagesNotVisible]
            }, cancellationToken).ConfigureAwait(false);

            results.Add(new QueueStats
            {
                QueueName = queueName,
                ActiveCount = ReadCount(response.Attributes, QueueAttributeName.ApproximateNumberOfMessages),
                InFlightCount = ReadCount(response.Attributes, QueueAttributeName.ApproximateNumberOfMessagesNotVisible),
                DeadLetterCount = await GetDeadLetterCountAsync(queueName, cancellationToken).ConfigureAwait(false)
            });
        }

        return results;
    }

    private async Task<long> GetDeadLetterCountAsync(string queueName, CancellationToken cancellationToken)
    {
        var dlqName = QueueDefinition.DeadLetterQueueNameFor(queueName);
        var now = _timeProvider.GetUtcNow();

        // A missing dead-letter queue is remembered for a minute so metrics polling does not hammer GetQueueUrl.
        if (_dlqNotFound.TryGetValue(dlqName, out var markedAt))
        {
            if (now - markedAt < s_dlqNotFoundTtl)
                return 0;

            _dlqNotFound.TryRemove(dlqName, out _);
        }

        try
        {
            if (!_queueUrlCache.TryGetValue(dlqName, out var dlqUrl))
            {
                var dlqResponse = await _sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = dlqName }, cancellationToken).ConfigureAwait(false);
                dlqUrl = dlqResponse.QueueUrl;
                _queueUrlCache[dlqName] = dlqUrl;
            }

            var dlqAttributes = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = dlqUrl,
                AttributeNames = [QueueAttributeName.ApproximateNumberOfMessages]
            }, cancellationToken).ConfigureAwait(false);

            return ReadCount(dlqAttributes.Attributes, QueueAttributeName.ApproximateNumberOfMessages);
        }
        catch (QueueDoesNotExistException)
        {
            _dlqNotFound[dlqName] = now;
            _queueUrlCache.TryRemove(dlqName, out _);
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to read dead-letter depth for {QueueName}", dlqName);
            return 0;
        }
    }

    private static long ReadCount(Dictionary<string, string>? attributes, string name)
        => attributes is not null && attributes.TryGetValue(name, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static Dictionary<string, MessageAttributeValue> ToMessageAttributes(Dictionary<string, string>? headers)
    {
        var attributes = new Dictionary<string, MessageAttributeValue>(headers?.Count ?? 0);
        if (headers is null)
            return attributes;

        foreach (var (key, value) in headers)
            attributes[key] = new MessageAttributeValue { DataType = "String", StringValue = value };

        return attributes;
    }

    private static int ToSeconds(TimeSpan value) => (int)Math.Ceiling(value.TotalSeconds);

    private static Message GetNativeMessage(QueueMessage message)
        => message.NativeMessage as Message
           ?? throw new InvalidOperationException(
               "QueueMessage.NativeMessage is not an SQS Message. This QueueMessage was not created by SqsQueueClient.");
}
