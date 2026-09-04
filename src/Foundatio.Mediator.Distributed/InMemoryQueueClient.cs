using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// In-memory <see cref="IQueueClient"/> backed by <see cref="Channel{T}"/>.
/// Intended for development and testing. Does not support visibility timeouts
/// or dead-letter semantics — <see cref="CompleteAsync"/> and <see cref="RenewTimeoutAsync"/>
/// are no-ops, and <see cref="AbandonAsync"/> re-enqueues the message immediately.
/// </summary>
public sealed class InMemoryQueueClient : IQueueClient
{
    private readonly ConcurrentDictionary<string, Channel<InMemoryEntry>> _channels = new();
    private readonly ConcurrentDictionary<string, Channel<InMemoryEntry>> _deadLetterChannels = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryQueueClient(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
    {
        var channel = GetOrCreateChannel(queueName);
        foreach (var entry in entries)
        {
            var internalEntry = new InMemoryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Body = entry.Body,
                Headers = entry.Headers != null ? new Dictionary<string, string>(entry.Headers) : new(),
                DequeueCount = 0,
                EnqueuedAt = _timeProvider.GetUtcNow()
            };

            await channel.Writer.WriteAsync(internalEntry, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, CancellationToken cancellationToken = default)
    {
        var channel = GetOrCreateChannel(queueName);
        var results = new List<QueueMessage>(maxCount);

        // Wait for at least one message
        InMemoryEntry first;
        try
        {
            first = await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return results;
        }

        var now = _timeProvider.GetUtcNow();
        first.IncrementDequeueCount();
        results.Add(ToQueueMessage(first, queueName, now));

        // Try to read more without waiting
        while (results.Count < maxCount && channel.Reader.TryRead(out var entry))
        {
            entry.IncrementDequeueCount();
            results.Add(ToQueueMessage(entry, queueName, now));
        }

        return results;
    }

    public Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask; // Already consumed from channel

    public async Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default)
    {
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);

        // Re-enqueue with the existing dequeue count (already incremented)
        var channel = GetOrCreateChannel(message.QueueName);
        var entry = new InMemoryEntry
        {
            Id = message.Id,
            Body = message.Body,
            Headers = new Dictionary<string, string>(message.Headers),
            DequeueCount = message.DequeueCount,
            EnqueuedAt = message.EnqueuedAt
        };

        await channel.Writer.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default)
        => Task.CompletedTask; // No visibility timeout concept in-memory

    public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default)
    {
        var dlqChannel = GetOrCreateDeadLetterChannel(message.QueueName);
        var entry = new InMemoryEntry
        {
            Id = message.Id,
            Body = message.Body,
            Headers = new Dictionary<string, string>(message.Headers)
            {
                [MessageHeaders.DeadLetterReason] = reason,
                [MessageHeaders.DeadLetteredAt] = _timeProvider.GetUtcNow().ToString("O"),
                [MessageHeaders.OriginalQueueName] = message.QueueName,
                [MessageHeaders.DeadLetterDequeueCount] = message.DequeueCount.ToString()
            },
            DequeueCount = message.DequeueCount,
            EnqueuedAt = message.EnqueuedAt
        };

        return dlqChannel.Writer.WriteAsync(entry, cancellationToken).AsTask();
    }

    /// <summary>
    /// Gets the number of messages in the dead-letter queue for the specified queue.
    /// Intended for testing assertions.
    /// </summary>
    public int GetDeadLetterCount(string queueName)
    {
        if (!_deadLetterChannels.TryGetValue(queueName, out var channel))
            return 0;

        return channel.Reader.Count;
    }

    /// <summary>
    /// Reads all messages currently in the dead-letter queue for the specified queue.
    /// Messages are consumed (removed) from the DLQ. Intended for testing assertions.
    /// </summary>
    public IReadOnlyList<QueueMessage> DrainDeadLetterMessages(string queueName)
    {
        if (!_deadLetterChannels.TryGetValue(queueName, out var channel))
            return [];

        var messages = new List<QueueMessage>();
        var now = _timeProvider.GetUtcNow();
        while (channel.Reader.TryRead(out var entry))
        {
            messages.Add(ToQueueMessage(entry, $"{queueName}-dead-letter", now));
        }

        return messages;
    }

    private Channel<InMemoryEntry> GetOrCreateChannel(string queueName)
        => _channels.GetOrAdd(queueName, _ => Channel.CreateUnbounded<InMemoryEntry>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));

    private Channel<InMemoryEntry> GetOrCreateDeadLetterChannel(string queueName)
        => _deadLetterChannels.GetOrAdd(queueName, _ => Channel.CreateUnbounded<InMemoryEntry>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));

    /// <inheritdoc />
    public Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
    {
        var results = new List<QueueStats>(queueNames.Count);
        foreach (var queueName in queueNames)
        {
            int activeCount = 0;
            if (_channels.TryGetValue(queueName, out var channel))
                activeCount = channel.Reader.Count;

            int deadLetterCount = 0;
            if (_deadLetterChannels.TryGetValue(queueName, out var dlqChannel))
                deadLetterCount = dlqChannel.Reader.Count;

            results.Add(new QueueStats
            {
                QueueName = queueName,
                ActiveCount = activeCount,
                DeadLetterCount = deadLetterCount
            });
        }

        return Task.FromResult<IReadOnlyList<QueueStats>>(results);
    }

    private static QueueMessage ToQueueMessage(InMemoryEntry entry, string queueName, DateTimeOffset dequeuedAt) => new()
    {
        Id = entry.Id,
        Body = entry.Body,
        Headers = entry.Headers,
        QueueName = queueName,
        DequeueCount = entry.DequeueCount,
        EnqueuedAt = entry.EnqueuedAt,
        DequeuedAt = dequeuedAt
    };

    private sealed class InMemoryEntry
    {
        public required string Id { get; init; }
        public required ReadOnlyMemory<byte> Body { get; init; }
        public required Dictionary<string, string> Headers { get; init; }
        private int _dequeueCount;
        public int DequeueCount { get => _dequeueCount; set => _dequeueCount = value; }
        public int IncrementDequeueCount() => Interlocked.Increment(ref _dequeueCount);
        public DateTimeOffset EnqueuedAt { get; init; }
    }
}
