using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// In-memory <see cref="IQueueClient"/> for development and tests. It models the same lease
/// semantics as a real transport: a received message is invisible until it is completed,
/// abandoned, or its visibility timeout lapses, and abandons with a delay are scheduled rather
/// than awaited. Timers come from the supplied <see cref="TimeProvider"/>, so tests can use a fake clock.
/// </summary>
public sealed class InMemoryQueueClient : IQueueClient
{
    /// <inheritdoc />
    public bool IsDistributed => false;

    private readonly ConcurrentDictionary<string, InMemoryQueue> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;

    public InMemoryQueueClient(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Visibility timeout used when <see cref="ReceiveAsync(string,int,TimeSpan?,CancellationToken)"/> is called without one.
    /// </summary>
    public TimeSpan DefaultVisibilityTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var queue = GetQueue(queueName);
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in entries)
        {
            queue.Enqueue(new InMemoryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Body = entry.Body,
                Headers = entry.Headers is not null ? new Dictionary<string, string>(entry.Headers) : new(),
                EnqueuedAt = now
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Receives using <see cref="DefaultVisibilityTimeout"/>.
    /// </summary>
    public Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, CancellationToken cancellationToken = default)
        => ReceiveAsync(queueName, maxCount, null, cancellationToken);

    public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        var queue = GetQueue(queueName);
        var results = new List<QueueMessage>(Math.Max(1, maxCount));

        var timeout = visibilityTimeout ?? DefaultVisibilityTimeout;
        try
        {
            while (results.Count == 0 && await queue.Ready.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (results.Count < maxCount && queue.TryLease(timeout, _timeProvider) is { } message)
                    results.Add(message);
            }
        }
        catch (OperationCanceledException) { }

        return results;
    }

    public Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetQueue(message.QueueName).Complete(message);
        return Task.CompletedTask;
    }

    public Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetQueue(message.QueueName).Abandon(message, delay, _timeProvider);
        return Task.CompletedTask;
    }

    public Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetQueue(message.QueueName).Renew(message, extension);
        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Claim this receipt before copying to the DLQ: an expired worker cannot dead-letter newer work.
        GetQueue(message.QueueName).Complete(message);
        var headers = new Dictionary<string, string>(message.Headers)
        {
            [MessageHeaders.DeadLetterReason] = reason,
            [MessageHeaders.DeadLetteredAt] = _timeProvider.GetUtcNow().ToString("O"),
            [MessageHeaders.OriginalQueueName] = message.QueueName,
            [MessageHeaders.DeadLetterDequeueCount] = message.DequeueCount.ToString()
        };

        return SendAsync(QueueDefinition.DeadLetterQueueNameFor(message.QueueName), [new QueueEntry { Body = message.Body, Headers = headers }], CancellationToken.None);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
    {
        var results = new List<QueueStats>(queueNames.Count);
        foreach (var queueName in queueNames)
        {
            results.Add(GetQueue(queueName).Snapshot(GetDeadLetterCount(queueName)));
        }

        return Task.FromResult<IReadOnlyList<QueueStats>>(results);
    }

    /// <summary>
    /// Number of messages waiting to be received.
    /// </summary>
    public int GetPendingCount(string queueName)
        => _queues.TryGetValue(queueName, out var queue) ? queue.Ready.Reader.Count : 0;

    /// <summary>Number of messages scheduled for later delivery.</summary>
    public int GetDelayedCount(string queueName)
        => _queues.TryGetValue(queueName, out var queue) ? queue.DelayedCount : 0;

    /// <summary>
    /// Number of received messages that have not been completed, abandoned, or expired.
    /// </summary>
    public int GetInFlightCount(string queueName)
        => _queues.TryGetValue(queueName, out var queue) ? queue.InFlightCount : 0;

    /// <summary>
    /// Number of messages waiting in the dead-letter queue of <paramref name="queueName"/>.
    /// </summary>
    public int GetDeadLetterCount(string queueName)
        => GetPendingCount(QueueDefinition.DeadLetterQueueNameFor(queueName));

    /// <summary>
    /// Removes and returns every message waiting in the dead-letter queue of <paramref name="queueName"/>.
    /// </summary>
    public IReadOnlyList<QueueMessage> DrainDeadLetterMessages(string queueName)
    {
        var dlqName = QueueDefinition.DeadLetterQueueNameFor(queueName);
        if (!_queues.TryGetValue(dlqName, out var queue))
            return [];

        var now = _timeProvider.GetUtcNow();
        var messages = new List<QueueMessage>();
        while (queue.Ready.Reader.TryRead(out var entry))
            messages.Add(entry.ToMessage(dlqName, now));

        return messages;
    }

    private InMemoryQueue GetQueue(string queueName)
        => _queues.GetOrAdd(queueName, static name => new InMemoryQueue(name));

    public ValueTask DisposeAsync()
    {
        foreach (var queue in _queues.Values)
            queue.Dispose();
        _queues.Clear();
        return default;
    }

    private sealed class InMemoryQueue(string name) : IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Lease> _inFlight = new();
        private readonly Dictionary<Guid, ITimer> _scheduled = new();

        public Channel<InMemoryEntry> Ready { get; } = Channel.CreateUnbounded<InMemoryEntry>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

        public int InFlightCount { get { lock (_gate) return _inFlight.Count; } }
        public int DelayedCount { get { lock (_gate) return _scheduled.Count; } }

        public void Enqueue(InMemoryEntry entry) { lock (_gate) Ready.Writer.TryWrite(entry); }

        public QueueStats Snapshot(int deadLetters)
        {
            lock (_gate)
                return new QueueStats { QueueName = name, ActiveCount = Ready.Reader.Count,
                    InFlightCount = _inFlight.Count, DelayedCount = _scheduled.Count, DeadLetterCount = deadLetters };
        }

        public QueueMessage? TryLease(TimeSpan timeout, TimeProvider timeProvider)
        {
            lock (_gate)
                return Ready.Reader.TryRead(out var entry) ? Lease(entry, timeout, timeProvider) : null;
        }

        public QueueMessage Lease(InMemoryEntry entry, TimeSpan visibilityTimeout, TimeProvider timeProvider)
        {
            lock (_gate)
            {
                var dequeueCount = entry.IncrementDequeueCount();
                var lease = new Lease(entry);
                _inFlight.Add(entry.Id, lease);
                // Arm after publishing the receipt, including with a concurrently advancing fake clock.
                lease.Timer = timeProvider.CreateTimer(_ => Expire(entry.Id, lease), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                if (visibilityTimeout > TimeSpan.Zero)
                    lease.Timer.Change(visibilityTimeout, Timeout.InfiniteTimeSpan);
                return entry.ToMessage(name, timeProvider.GetUtcNow(), dequeueCount, lease);
            }
        }

        private Lease RequireLease(QueueMessage message)
        {
            if (!_inFlight.TryGetValue(message.Id, out var lease) || !ReferenceEquals(lease, message.NativeMessage))
                throw new QueueLeaseLostException($"Delivery {message.Id} on {name} no longer owns its lease.");
            return lease;
        }

        public void Complete(QueueMessage message)
        {
            lock (_gate)
            {
                var lease = RequireLease(message);
                _inFlight.Remove(message.Id);
                lease.Timer?.Dispose();
            }
        }

        public void Abandon(QueueMessage message, TimeSpan delay, TimeProvider timeProvider)
        {
            lock (_gate)
            {
                var lease = RequireLease(message);
                _inFlight.Remove(message.Id);
                lease.Timer?.Dispose();
                if (delay <= TimeSpan.Zero)
                {
                    Enqueue(lease.Entry);
                    return;
                }

                var key = Guid.NewGuid();
                var timer = timeProvider.CreateTimer(_ =>
                {
                    lock (_gate)
                    {
                        if (_scheduled.Remove(key, out var scheduled))
                        {
                            Enqueue(lease.Entry);
                            scheduled.Dispose();
                        }
                    }
                }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _scheduled.Add(key, timer);
                timer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }

        public void Renew(QueueMessage message, TimeSpan extension)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(extension, TimeSpan.Zero);
            lock (_gate)
                RequireLease(message).Timer?.Change(extension, Timeout.InfiniteTimeSpan);
        }

        private void Expire(string id, Lease lease)
        {
            lock (_gate)
            {
                if (_inFlight.TryGetValue(id, out var current) && ReferenceEquals(current, lease))
                {
                    _inFlight.Remove(id);
                    Enqueue(lease.Entry);
                    lease.Timer?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (var lease in _inFlight.Values)
                    lease.Timer?.Dispose();
                foreach (var timer in _scheduled.Values)
                    timer.Dispose();
                _inFlight.Clear();
                _scheduled.Clear();
                Ready.Writer.TryComplete();
            }
        }
    }

    private sealed class Lease(InMemoryEntry entry)
    {
        public InMemoryEntry Entry { get; } = entry;
        public ITimer? Timer { get; set; }
    }

    private sealed class InMemoryEntry
    {
        private int _dequeueCount;

        public required string Id { get; init; }
        public required ReadOnlyMemory<byte> Body { get; init; }
        public required Dictionary<string, string> Headers { get; init; }
        public DateTimeOffset EnqueuedAt { get; init; }
        public int DequeueCount => _dequeueCount;
        public int IncrementDequeueCount() => Interlocked.Increment(ref _dequeueCount);

        public QueueMessage ToMessage(string queueName, DateTimeOffset dequeuedAt, int? dequeueCount = null, object? receipt = null) => new()
        {
            Id = Id,
            Body = Body,
            Headers = Headers,
            QueueName = queueName,
            DequeueCount = dequeueCount ?? _dequeueCount,
            EnqueuedAt = EnqueuedAt,
            NativeMessage = receipt,
            DequeuedAt = dequeuedAt
        };
    }
}
