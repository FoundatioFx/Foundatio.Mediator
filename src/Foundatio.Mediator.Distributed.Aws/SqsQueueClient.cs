using System.Collections.Concurrent;
using System.Globalization;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// <see cref="IQueueClient"/> implementation backed by Amazon SQS.
/// Headers are mapped to SQS MessageAttributes. Body is sent as the MessageBody string
/// (base64-encoded from the raw bytes).
/// </summary>
public sealed class SqsQueueClient : IQueueClient
{
    private readonly IAmazonSQS _sqs;
    private readonly SqsQueueClientOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, string> _queueUrlCache = new();

    public SqsQueueClient(IAmazonSQS sqs, SqsQueueClientOptions? options = null, TimeProvider? timeProvider = null)
    {
        _sqs = sqs;
        _options = options ?? new SqsQueueClientOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task SendAsync(string queueName, QueueEntry entry, CancellationToken cancellationToken = default)
    {
        var queueUrl = await GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);

        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = Convert.ToBase64String(entry.Body.Span),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>()
        };

        if (entry.Headers is { Count: > 0 })
        {
            foreach (var (key, value) in entry.Headers)
            {
                request.MessageAttributes[key] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = value
                };
            }
        }

        await _sqs.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendBatchAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return;

        var queueUrl = await GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);

        // SQS batch limit is 10 messages
        for (int i = 0; i < entries.Count; i += 10)
        {
            var batch = new SendMessageBatchRequest
            {
                QueueUrl = queueUrl,
                Entries = []
            };

            var end = Math.Min(i + 10, entries.Count);
            for (int j = i; j < end; j++)
            {
                var entry = entries[j];
                var batchEntry = new SendMessageBatchRequestEntry
                {
                    Id = j.ToString(CultureInfo.InvariantCulture),
                    MessageBody = Convert.ToBase64String(entry.Body.Span),
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>()
                };

                if (entry.Headers is { Count: > 0 })
                {
                    foreach (var (key, value) in entry.Headers)
                    {
                        batchEntry.MessageAttributes[key] = new MessageAttributeValue
                        {
                            DataType = "String",
                            StringValue = value
                        };
                    }
                }

                batch.Entries.Add(batchEntry);
            }

            var response = await _sqs.SendMessageBatchAsync(batch, cancellationToken).ConfigureAwait(false);

            if (response.Failed is { Count: > 0 })
            {
                var first = response.Failed[0];
                throw new InvalidOperationException(
                    $"Failed to send {response.Failed.Count} message(s) to SQS queue '{queueName}': [{first.Code}] {first.Message}");
            }
        }
    }

    public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, CancellationToken cancellationToken = default)
    {
        var queueUrl = await GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);

        // SQS maximum is 10 messages per receive
        var request = new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = Math.Min(maxCount, 10),
            WaitTimeSeconds = _options.WaitTimeSeconds,
            MessageSystemAttributeNames = ["ApproximateReceiveCount", "SentTimestamp"],
            MessageAttributeNames = ["All"]
        };

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
                Body = Convert.FromBase64String(sqsMessage.Body),
                Headers = headers,
                QueueName = queueName,
                DequeueCount = dequeueCount,
                EnqueuedAt = enqueuedAt,
                DequeuedAt = now,
                NativeMessage = sqsMessage  // Carry the full SQS message for ReceiptHandle access
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

        var visibilityTimeout = Math.Max(0, (int)Math.Ceiling(delay.TotalSeconds));
        await _sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
        {
            QueueUrl = queueUrl,
            ReceiptHandle = sqsMessage.ReceiptHandle,
            VisibilityTimeout = visibilityTimeout
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default)
    {
        var dlqName = $"{message.QueueName}-dead-letter";

        // Build a new entry with original body + headers + dead-letter metadata
        var headers = new Dictionary<string, string>(message.Headers)
        {
            [MessageHeaders.DeadLetterReason] = reason,
            [MessageHeaders.DeadLetteredAt] = _timeProvider.GetUtcNow().ToString("O"),
            [MessageHeaders.OriginalQueueName] = message.QueueName,
            [MessageHeaders.DeadLetterDequeueCount] = message.DequeueCount.ToString()
        };

        var entry = new QueueEntry
        {
            Body = message.Body,
            Headers = headers
        };

        // Send to DLQ then complete the original message
        await SendAsync(dlqName, entry, cancellationToken).ConfigureAwait(false);
        await CompleteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default)
    {
        var queueUrl = await GetQueueUrlAsync(message.QueueName, cancellationToken).ConfigureAwait(false);
        var sqsMessage = GetNativeMessage(message);

        await _sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
        {
            QueueUrl = queueUrl,
            ReceiptHandle = sqsMessage.ReceiptHandle,
            VisibilityTimeout = (int)Math.Ceiling(extension.TotalSeconds)
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetQueueUrlAsync(string queueName, CancellationToken cancellationToken)
    {
        if (_queueUrlCache.TryGetValue(queueName, out var cached))
            return cached;

        if (_options.AutoCreateQueues)
        {
            var createResponse = await _sqs.CreateQueueAsync(new CreateQueueRequest
            {
                QueueName = queueName
            }, cancellationToken).ConfigureAwait(false);

            _queueUrlCache[queueName] = createResponse.QueueUrl;
            return createResponse.QueueUrl;
        }

        var response = await _sqs.GetQueueUrlAsync(new GetQueueUrlRequest
        {
            QueueName = queueName
        }, cancellationToken).ConfigureAwait(false);

        _queueUrlCache[queueName] = response.QueueUrl;
        return response.QueueUrl;
    }

    /// <inheritdoc />
    public Task EnsureQueuesAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
    {
        return Task.WhenAll(queueNames.Select(name => GetQueueUrlAsync(name, cancellationToken)));
    }

    /// <inheritdoc />
    public async Task<QueueStats> GetQueueStatsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        var queueUrl = await GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);

        var response = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = queueUrl,
            AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"]
        }, cancellationToken).ConfigureAwait(false);

        long activeCount = 0;
        if (response.Attributes.TryGetValue("ApproximateNumberOfMessages", out var activeStr)
            && long.TryParse(activeStr, out var parsedActive))
            activeCount = parsedActive;

        long inFlightCount = 0;
        if (response.Attributes.TryGetValue("ApproximateNumberOfMessagesNotVisible", out var inFlightStr)
            && long.TryParse(inFlightStr, out var parsedInFlight))
            inFlightCount = parsedInFlight;

        // Try to get dead-letter queue stats
        long deadLetterCount = 0;
        var dlqName = $"{queueName}-dead-letter";
        if (_queueUrlCache.ContainsKey(dlqName))
        {
            try
            {
                var dlqUrl = await GetQueueUrlAsync(dlqName, cancellationToken).ConfigureAwait(false);
                var dlqResponse = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
                {
                    QueueUrl = dlqUrl,
                    AttributeNames = ["ApproximateNumberOfMessages"]
                }, cancellationToken).ConfigureAwait(false);

                if (dlqResponse.Attributes.TryGetValue("ApproximateNumberOfMessages", out var dlqStr)
                    && long.TryParse(dlqStr, out var parsedDlq))
                    deadLetterCount = parsedDlq;
            }
            catch
            {
                // DLQ may not exist yet
            }
        }

        return new QueueStats
        {
            QueueName = queueName,
            ActiveCount = activeCount,
            InFlightCount = inFlightCount,
            DeadLetterCount = deadLetterCount
        };
    }

    private static Message GetNativeMessage(QueueMessage message)
        => message.NativeMessage as Message
           ?? throw new InvalidOperationException(
               "QueueMessage.NativeMessage is not an SQS Message. This QueueMessage was not created by SqsQueueClient.");
}
