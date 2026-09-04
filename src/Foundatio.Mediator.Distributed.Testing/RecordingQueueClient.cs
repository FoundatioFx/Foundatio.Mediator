using System.Collections.Concurrent;
using System.Text.Json;

namespace Foundatio.Mediator.Distributed.Testing;

/// <summary>
/// An <see cref="IQueueClient"/> that records every message sent through it, for asserting what a
/// handler enqueued, and can wait until the wrapped in-memory queues have been fully processed.
/// </summary>
/// <example>
/// <code>
/// var queue = new RecordingQueueClient();
/// services.AddSingleton&lt;IQueueClient&gt;(queue);
/// ...
/// await mediator.InvokeAsync(new CreateOrder(...));
/// var sent = Assert.Single(queue.Sent&lt;SendConfirmation&gt;());
/// await queue.DrainAsync();               // workers have processed everything
/// </code>
/// </example>
public sealed class RecordingQueueClient : IQueueClient
{
    private readonly IQueueClient _inner;
    private readonly InMemoryQueueClient? _inMemory;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentQueue<SentMessage> _sent = new();
    private readonly ConcurrentDictionary<string, byte> _queueNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Wraps an in-memory queue client so tests get real lease semantics and <see cref="DrainAsync"/>.
    /// </summary>
    public RecordingQueueClient(JsonSerializerOptions? jsonOptions = null, TimeProvider? timeProvider = null)
        : this(new InMemoryQueueClient(timeProvider), jsonOptions, timeProvider)
    {
    }

    /// <summary>
    /// Wraps any queue client. <see cref="DrainAsync"/> then polls the transport's statistics.
    /// </summary>
    public RecordingQueueClient(IQueueClient inner, JsonSerializerOptions? jsonOptions = null, TimeProvider? timeProvider = null)
    {
        _inner = inner;
        _inMemory = inner as InMemoryQueueClient;
        _jsonOptions = jsonOptions ?? JsonSerializerOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// The wrapped client.
    /// </summary>
    public IQueueClient Inner => _inner;

    /// <summary>
    /// Every message sent, in order.
    /// </summary>
    public IReadOnlyList<SentMessage> SentMessages => _sent.ToArray();

    /// <summary>
    /// Sent messages whose type header names <typeparamref name="T"/>, deserialized.
    /// </summary>
    public IReadOnlyList<T> Sent<T>()
    {
        var typeName = typeof(T).FullName;
        var results = new List<T>();
        foreach (var sent in _sent)
        {
            if (!string.Equals(sent.MessageType, typeName, StringComparison.Ordinal))
                continue;

            if (JsonSerializer.Deserialize<T>(sent.Body.Span, _jsonOptions) is { } message)
                results.Add(message);
        }

        return results;
    }

    /// <summary>
    /// Whether at least one message of <typeparamref name="T"/> was sent.
    /// </summary>
    public bool WasSent<T>() => _sent.Any(s => string.Equals(s.MessageType, typeof(T).FullName, StringComparison.Ordinal));

    /// <summary>
    /// Forgets recorded messages. Queue contents are untouched.
    /// </summary>
    public void Clear() => _sent.Clear();

    /// <summary>
    /// Waits until every queue that has seen a send has no pending or in-flight messages, so workers
    /// have finished processing. Dead-letter queues are not drained.
    /// </summary>
    public async Task DrainAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var deadline = _timeProvider.GetUtcNow() + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            if (await IsIdleAsync(cancellationToken).ConfigureAwait(false))
                return;

            if (_timeProvider.GetUtcNow() >= deadline)
                throw new TimeoutException($"Queues were not drained within {timeout ?? TimeSpan.FromSeconds(30)}: {string.Join(", ", await PendingSummaryAsync(cancellationToken).ConfigureAwait(false))}");

            await Task.Delay(TimeSpan.FromMilliseconds(25), _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> IsIdleAsync(CancellationToken cancellationToken)
    {
        var queues = _queueNames.Keys.Where(q => !q.EndsWith("-dead-letter", StringComparison.OrdinalIgnoreCase)).ToList();
        if (queues.Count == 0)
            return true;

        if (_inMemory is not null)
            return queues.All(q => _inMemory.GetPendingCount(q) == 0 && _inMemory.GetInFlightCount(q) == 0);

        var stats = await _inner.GetQueueStatsAsync(queues, cancellationToken).ConfigureAwait(false);
        return stats.All(s => s.ActiveCount == 0 && s.InFlightCount == 0);
    }

    private async Task<IEnumerable<string>> PendingSummaryAsync(CancellationToken cancellationToken)
    {
        var queues = _queueNames.Keys.ToList();
        if (_inMemory is not null)
            return queues.Select(q => $"{q}: {_inMemory.GetPendingCount(q)} pending, {_inMemory.GetInFlightCount(q)} in flight");

        var stats = await _inner.GetQueueStatsAsync(queues, cancellationToken).ConfigureAwait(false);
        return stats.Select(s => $"{s.QueueName}: {s.ActiveCount} pending, {s.InFlightCount} in flight");
    }

    public Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
    {
        _queueNames.TryAdd(queueName, 0);
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in entries)
        {
            _sent.Enqueue(new SentMessage(
                queueName,
                entry.Body,
                entry.Headers is not null ? new Dictionary<string, string>(entry.Headers) : new Dictionary<string, string>(),
                now));
        }

        return _inner.SendAsync(queueName, entries, cancellationToken);
    }

    public Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken cancellationToken = default)
    {
        _queueNames.TryAdd(queueName, 0);
        return _inner.ReceiveAsync(queueName, maxCount, visibilityTimeout, cancellationToken);
    }

    public Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default) => _inner.CompleteAsync(message, cancellationToken);
    public Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default) => _inner.AbandonAsync(message, delay, cancellationToken);
    public Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default) => _inner.RenewTimeoutAsync(message, extension, cancellationToken);
    public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default) => _inner.DeadLetterAsync(message, reason, cancellationToken);
    public Task EnsureQueuesAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken cancellationToken = default) => _inner.EnsureQueuesAsync(queues, cancellationToken);
    public Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default) => _inner.GetQueueStatsAsync(queueNames, cancellationToken);
    public Task<IReadOnlyList<QueueMessage>> ReceiveDeadLettersAsync(string queueName, int maxCount, CancellationToken cancellationToken = default) => _inner.ReceiveDeadLettersAsync(queueName, maxCount, cancellationToken);
    public Task ReplayAsync(QueueMessage deadLetter, CancellationToken cancellationToken = default) => _inner.ReplayAsync(deadLetter, cancellationToken);
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>
/// A message recorded by <see cref="RecordingQueueClient"/>.
/// </summary>
public sealed record SentMessage(string QueueName, ReadOnlyMemory<byte> Body, IReadOnlyDictionary<string, string> Headers, DateTimeOffset SentAt)
{
    /// <summary>
    /// The <see cref="MessageHeaders.MessageType"/> header, if present.
    /// </summary>
    public string? MessageType => Headers.GetValueOrDefault(MessageHeaders.MessageType);

    /// <summary>
    /// The <see cref="MessageHeaders.JobId"/> header, if present.
    /// </summary>
    public string? JobId => Headers.GetValueOrDefault(MessageHeaders.JobId);
}
