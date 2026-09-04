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
        var queue = GetQueue(queueName);
        var results = new List<QueueMessage>(Math.Max(1, maxCount));

        InMemoryEntry first;
        try
        {
            first = await queue.Ready.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return results;
        }

        var timeout = visibilityTimeout ?? DefaultVisibilityTimeout;
        results.Add(queue.Lease(first, timeout, _timeProvider));

        while (results.Count < maxCount && queue.Ready.Reader.TryRead(out var entry))
            results.Add(queue.Lease(entry, timeout, _timeProvider));

        return results;
    }

    public Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default)
    {
        GetQueue(message.QueueName).Complete(message.Id);
        return Task.CompletedTask;
    }

    public Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default)
    {
        GetQueue(message.QueueName).Abandon(message.Id, delay, _timeProvider);
        return Task.CompletedTask;
    }

    public Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default)
    {
        GetQueue(message.QueueName).Renew(message.Id, extension);
        return Task.CompletedTask;
    }

    public async Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>(message.Headers)
        {
            [MessageHeaders.DeadLetterReason] = reason,
            [MessageHeaders.DeadLetteredAt] = _timeProvider.GetUtcNow().ToString("O"),
            [MessageHeaders.OriginalQueueName] = message.QueueName,
            [MessageHeaders.DeadLetterDequeueCount] = message.DequeueCount.ToString()
        };

        await SendAsync(QueueDefinition.DeadLetterQueueNameFor(message.QueueName), [new QueueEntry { Body = message.Body, Headers = headers }], cancellationToken).ConfigureAwait(false);
        await CompleteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
    {
        var results = new List<QueueStats>(queueNames.Count);
        foreach (var queueName in queueNames)
        {
            results.Add(new QueueStats
            {
                QueueName = queueName,
                ActiveCount = GetPendingCount(queueName),
                InFlightCount = GetInFlightCount(queueName),
                DeadLetterCount = GetDeadLetterCount(queueName)
            });
        }

        return Task.FromResult<IReadOnlyList<QueueStats>>(results);
    }

    /// <summary>
    /// Number of messages waiting to be received.
    /// </summary>
    public int GetPendingCount(string queueName)
        => _queues.TryGetValue(queueName, out var queue) ? queue.Ready.Reader.Count : 0;

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
        private readonly ConcurrentDictionary<string, Lease> _inFlight = new();
        private readonly ConcurrentDictionary<Guid, ITimer> _scheduled = new();

        public Channel<InMemoryEntry> Ready { get; } = Channel.CreateUnbounded<InMemoryEntry>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

        public int InFlightCount => _inFlight.Count;

        public void Enqueue(InMemoryEntry entry) => Ready.Writer.TryWrite(entry);

        public QueueMessage Lease(InMemoryEntry entry, TimeSpan visibilityTimeout, TimeProvider timeProvider)
        {
            var dequeueCount = entry.IncrementDequeueCount();
            var lease = new Lease(entry);
            _inFlight[entry.Id] = lease;

            if (visibilityTimeout > TimeSpan.Zero)
                lease.Timer = timeProvider.CreateTimer(_ => Expire(entry.Id, lease), null, visibilityTimeout, Timeout.InfiniteTimeSpan);

            return entry.ToMessage(name, timeProvider.GetUtcNow(), dequeueCount);
        }

        public void Complete(string id)
        {
            if (_inFlight.TryRemove(id, out var lease))
                lease.Timer?.Dispose();
        }

        public void Abandon(string id, TimeSpan delay, TimeProvider timeProvider)
        {
            if (!_inFlight.TryRemove(id, out var lease))
                return;

            lease.Timer?.Dispose();

            if (delay <= TimeSpan.Zero)
            {
                Enqueue(lease.Entry);
                return;
            }

            var key = Guid.NewGuid();
            ITimer? timer = null;
            timer = timeProvider.CreateTimer(_ =>
            {
                if (_scheduled.TryRemove(key, out var t))
                    t.Dispose();
                Enqueue(lease.Entry);
            }, null, delay, Timeout.InfiniteTimeSpan);
            _scheduled[key] = timer;
        }

        public void Renew(string id, TimeSpan extension)
        {
            if (_inFlight.TryGetValue(id, out var lease) && extension > TimeSpan.Zero)
                lease.Timer?.Change(extension, Timeout.InfiniteTimeSpan);
        }

        private void Expire(string id, Lease lease)
        {
            if (_inFlight.TryRemove(new KeyValuePair<string, Lease>(id, lease)))
            {
                lease.Timer?.Dispose();
                Enqueue(lease.Entry);
            }
        }

        public void Dispose()
        {
            foreach (var lease in _inFlight.Values)
                lease.Timer?.Dispose();
            foreach (var timer in _scheduled.Values)
                timer.Dispose();
            Ready.Writer.TryComplete();
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

        public QueueMessage ToMessage(string queueName, DateTimeOffset dequeuedAt, int? dequeueCount = null) => new()
        {
            Id = Id,
            Body = Body,
            Headers = Headers,
            QueueName = queueName,
            DequeueCount = dequeueCount ?? _dequeueCount,
            EnqueuedAt = EnqueuedAt,
            DequeuedAt = dequeuedAt
        };
    }
}
