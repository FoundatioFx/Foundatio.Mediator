using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>Each entry retains its own completion; cancellation never cancels a peer's HTTP request.</summary>
internal sealed class AwsBatcher<T> : IAsyncDisposable
{
    private readonly AwsBatchOptions _options;
    private readonly Func<IReadOnlyList<Entry>, CancellationToken, Task> _send;
    private readonly Channel<Entry> _channel;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots;
    private readonly Task _pump;
    private int _disposed;

    public AwsBatcher(AwsBatchOptions options, Func<IReadOnlyList<Entry>, CancellationToken, Task> send)
    {
        _options = options;
        _send = send;
        _slots = new SemaphoreSlim(options.MaxConcurrency);
        _channel = Channel.CreateBounded<Entry>(new BoundedChannelOptions(options.Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        // A destination outlives the request that first uses it. Never retain that request's
        // Activity, tenant identity, or other AsyncLocal state in the transport pump.
        if (ExecutionContext.IsFlowSuppressed()) _pump = Task.Run(RunAsync);
        else
        {
            using (ExecutionContext.SuppressFlow()) _pump = Task.Run(RunAsync);
        }
    }

    public async Task AddAsync(T value, int bytes, CancellationToken ct, int batchSizeHint = int.MaxValue)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ct.ThrowIfCancellationRequested();
        var entry = new Entry(value, bytes, ct, batchSizeHint);
        try
        {
            await _channel.Writer.WriteAsync(entry, ct).ConfigureAwait(false);
            await entry.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            entry.Completion.TrySetCanceled(ct);
            throw;
        }
        catch (ChannelClosedException) { throw new ObjectDisposedException(nameof(AwsBatcher<T>)); }
    }

    private async Task RunAsync()
    {
        var active = new List<Task>(_options.MaxConcurrency);
        Entry? carry = null;
        try
        {
            while (carry is not null || await _channel.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                await _slots.WaitAsync(_stop.Token).ConfigureAwait(false);
                var batch = new List<Entry>(_options.MaxBatchSize);
                try
                {
                    int bytes = 0;
                    int targetSize = _options.MaxBatchSize;
                    using var fill = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    fill.CancelAfter(_options.MaxDelay);
                    while (batch.Count < targetSize)
                    {
                        Entry? entry = carry;
                        carry = null;
                        if (entry is null && !_channel.Reader.TryRead(out entry))
                        {
                            if (_options.MaxDelay == TimeSpan.Zero || fill.IsCancellationRequested) break;
                            try
                            {
                                if (!await _channel.Reader.WaitToReadAsync(fill.Token).ConfigureAwait(false)) break;
                            }
                            catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { break; }
                            continue;
                        }
                        if (entry.CancellationToken.IsCancellationRequested)
                        {
                            entry.Completion.TrySetCanceled(entry.CancellationToken);
                            continue;
                        }
                        if (batch.Count > 0 && bytes + entry.Bytes > SqsPayload.MaxMessageBytes)
                        {
                            carry = entry;
                            break;
                        }
                        batch.Add(entry);
                        bytes += entry.Bytes;
                        targetSize = Math.Min(targetSize, entry.BatchSizeHint);
                    }
                    if (batch.Count == 0) _slots.Release();
                    else
                    {
                        active.RemoveAll(static task => task.IsCompleted);
                        active.Add(SendAsync(batch));
                    }
                }
                catch
                {
                    foreach (var entry in batch) entry.Completion.TrySetException(new ObjectDisposedException(nameof(AwsBatcher<T>)));
                    _slots.Release();
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally
        {
            if (carry is not null) carry.Completion.TrySetException(new ObjectDisposedException(nameof(AwsBatcher<T>)));
            while (_channel.Reader.TryRead(out var entry)) entry.Completion.TrySetException(new ObjectDisposedException(nameof(AwsBatcher<T>)));
            await Task.WhenAll(active).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(List<Entry> batch)
    {
        Activity? activity = null;
        try
        {
            // Recheck cancellation after collecting: a caller may have cancelled while the batch filled.
            for (int i = batch.Count - 1; i >= 0; i--)
                if (batch[i].CancellationToken.IsCancellationRequested)
                {
                    batch[i].Completion.TrySetCanceled(batch[i].CancellationToken);
                    batch.RemoveAt(i);
                }
            if (batch.Count == 0) return;
            activity = MediatorActivitySource.Instance.HasListeners()
                ? MediatorActivitySource.Instance.StartActivity("AWS batch", ActivityKind.Client, default(ActivityContext),
                    links: batch.Where(e => e.ActivityContext != default).Select(e => new ActivityLink(e.ActivityContext)))
                : null;
            activity?.SetTag("messaging.batch.message_count", batch.Count);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(_options.RequestTimeout);
            await _send(batch, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Exception failure = _stop.IsCancellationRequested ? new ObjectDisposedException(nameof(AwsBatcher<T>)) : ex;
            foreach (var entry in batch) entry.Completion.TrySetException(failure);
        }
        finally { activity?.Dispose(); _slots.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _channel.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _slots.Dispose();
        _stop.Dispose();
    }

    internal sealed class Entry(T value, int bytes, CancellationToken cancellationToken, int batchSizeHint)
    {
        public T Value { get; } = value;
        public ActivityContext ActivityContext { get; } = Activity.Current?.Context ?? default;
        public int Bytes { get; } = bytes;
        public int BatchSizeHint { get; } = batchSizeHint;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal sealed class AwsBatchers<T>(AwsBatchOptions options, Func<string, IReadOnlyList<AwsBatcher<T>.Entry>, CancellationToken, Task> send) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, AwsBatcher<T>> _batches = new();
    private readonly Lock _sync = new();
    private bool _disposed;

    public AwsBatcher<T> Get(string destination)
    {
        if (_batches.TryGetValue(destination, out var batcher)) return batcher;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_batches.TryGetValue(destination, out batcher))
            {
                batcher = new AwsBatcher<T>(options, (entries, ct) => send(destination, entries, ct));
                _batches[destination] = batcher;
            }
            return batcher;
        }
    }

    public async ValueTask DisposeAsync()
    {
        AwsBatcher<T>[] batches;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            batches = _batches.Values.ToArray();
        }
        await Task.WhenAll(batches.Select(b => b.DisposeAsync().AsTask())).ConfigureAwait(false);
    }
}

internal static class AwsBatchResults
{
    public static void Complete<T>(IReadOnlyList<AwsBatcher<T>.Entry> entries, IEnumerable<string> successful,
        IEnumerable<(string Id, string Code, string Message)> failed, string operation, string destination)
    {
        var successes = successful.ToHashSet(StringComparer.Ordinal);
        var failures = failed.ToDictionary(e => e.Id, StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
        {
            string id = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (failures.TryGetValue(id, out var failure))
            {
                Activity.Current?.SetStatus(ActivityStatusCode.Error, failure.Code);
                entries[i].Completion.TrySetException(new InvalidOperationException($"AWS {operation} failed for '{destination}': [{failure.Code}] {failure.Message}"));
            }
            else if (successes.Contains(id)) entries[i].Completion.TrySetResult();
            else
            {
                Activity.Current?.SetStatus(ActivityStatusCode.Error, "Missing batch result");
                entries[i].Completion.TrySetException(new InvalidOperationException($"AWS {operation} returned no result for entry {id} on '{destination}'; its outcome is unknown."));
            }
        }
    }
}
