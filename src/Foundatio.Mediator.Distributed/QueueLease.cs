using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>Owns one receipt from receive through settlement, independently of job-state storage.</summary>
internal sealed class QueueLease : IAsyncDisposable
{
    private readonly IQueueClient _client;
    private readonly QueueMessage _message;
    private readonly TimeSpan _visibilityTimeout;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _renewalStop = new();
    private readonly CancellationTokenSource _handlerStop;
    private readonly SemaphoreSlim _renewGate = new(1);
    private readonly Task _monitor;
    private long _expiresUtcTicks;
    private int _lost;

    public QueueLease(IQueueClient client, QueueMessage message, TimeSpan visibilityTimeout, bool autoRenew,
        TimeProvider time, ILogger logger, CancellationToken drainToken)
    {
        _client = client;
        _message = message;
        _visibilityTimeout = visibilityTimeout;
        _time = time;
        _logger = logger;
        _handlerStop = CancellationTokenSource.CreateLinkedTokenSource(drainToken);
        // Transport timestamps can use a different clock. The receive call established the lease.
        _expiresUtcTicks = (time.GetUtcNow() + visibilityTimeout).UtcTicks;
        _monitor = MonitorAsync(autoRenew);
    }

    public CancellationToken Token => _handlerStop.Token;
    public bool IsLost => Volatile.Read(ref _lost) != 0;
    public void Cancel() => _handlerStop.Cancel();
    public void StopRenewing() => _renewalStop.Cancel();

    public async Task RenewAsync(TimeSpan extension, CancellationToken cancellationToken)
    {
        await _renewGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsLost)
                throw new QueueLeaseLostException($"Lease lost for {_message.Id} on {_message.QueueName}.");

            var remaining = Remaining;
            if (remaining <= TimeSpan.Zero)
                throw new QueueLeaseLostException($"Lease expired for {_message.Id} on {_message.QueueName}.");

            // Use request start for a conservative deadline, not response time.
            var started = _time.GetUtcNow();
            await QueueOperation.RunAsync(ct => _client.RenewTimeoutAsync(_message, extension, ct),
                remaining, _time, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _expiresUtcTicks, (started + extension).UtcTicks);
        }
        finally
        {
            _renewGate.Release();
        }
    }

    private TimeSpan Remaining => new(Interlocked.Read(ref _expiresUtcTicks) - _time.GetUtcNow().UtcTicks);

    private async Task MonitorAsync(bool autoRenew)
    {
        var token = _renewalStop.Token;
        var retry = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var remaining = Remaining;
                if (remaining <= TimeSpan.Zero)
                    break;
                var delay = !autoRenew ? remaining : retry
                    ? TimeSpan.FromTicks(Math.Min(TimeSpan.TicksPerSecond, remaining.Ticks / 4))
                    : remaining / 2;
                await Task.Delay(delay, _time, token).ConfigureAwait(false);
                if (!autoRenew || Remaining <= TimeSpan.Zero)
                    break;

                try
                {
                    await RenewAsync(_visibilityTimeout, token).ConfigureAwait(false);
                    retry = false;
                }
                catch (QueueLeaseLostException)
                {
                    break;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    retry = true;
                    _logger.LogWarning(ex, "Lease renewal failed for {MessageId} on {QueueName}; retrying within the remaining lease", _message.Id, _message.QueueName);
                }
            }

            if (!token.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _lost, 1);
                _logger.LogWarning("Lease lost for {MessageId} on {QueueName}; cancelling processing", _message.Id, _message.QueueName);
                await _handlerStop.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _renewalStop.CancelAsync().ConfigureAwait(false);
        await _monitor.ConfigureAwait(false);
        _renewalStop.Dispose();
        _handlerStop.Dispose();
        _renewGate.Dispose();
    }
}
