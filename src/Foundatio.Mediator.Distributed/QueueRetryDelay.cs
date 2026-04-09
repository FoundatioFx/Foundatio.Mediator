namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Computes retry delays for failed queue messages based on a <see cref="QueueRetryPolicy"/>.
/// </summary>
public static class QueueRetryDelay
{
    /// <summary>
    /// Computes the retry delay for a failed message based on the configured retry policy,
    /// base delay, and the number of times the message has been dequeued.
    /// </summary>
    /// <param name="policy">The retry policy to apply.</param>
    /// <param name="baseDelay">The base delay duration.</param>
    /// <param name="dequeueCount">The 1-based dequeue count (including the current attempt).</param>
    /// <returns>The computed delay, capped at 15 minutes.</returns>
    public static TimeSpan Compute(QueueRetryPolicy policy, TimeSpan baseDelay, int dequeueCount)
    {
        if (policy == QueueRetryPolicy.None)
            return TimeSpan.Zero;

        if (baseDelay <= TimeSpan.Zero)
            return TimeSpan.Zero;

        // dequeueCount is 1-based; first retry is after attempt 1
        int retryNumber = Math.Max(0, dequeueCount - 1);

        double delayMs = policy switch
        {
            QueueRetryPolicy.Fixed => baseDelay.TotalMilliseconds,
            QueueRetryPolicy.Exponential => baseDelay.TotalMilliseconds * Math.Pow(2, retryNumber),
            _ => 0
        };

        // Apply proportional jitter (±10% of the computed delay)
        double jitterRange = delayMs * 0.1;
        double jitter = (Random.Shared.NextDouble() * 2 - 1) * jitterRange;
        delayMs = Math.Max(0, delayMs + jitter);

        // Cap at 15 minutes to prevent unreasonably long delays
        return TimeSpan.FromMilliseconds(Math.Min(delayMs, TimeSpan.FromMinutes(15).TotalMilliseconds));
    }
}
