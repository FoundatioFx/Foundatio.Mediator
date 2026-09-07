using System.Globalization;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Computes retry delays for failed queue messages based on a <see cref="QueueRetryPolicy"/>.
/// </summary>
public static class QueueRetryDelay
{
    /// <summary>
    /// Delays are capped here so a misconfigured policy cannot park a message for hours.
    /// </summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    private const int MaxExponent = 20;

    /// <summary>
    /// Computes the delay before the next attempt.
    /// </summary>
    /// <param name="policy">The retry policy to apply.</param>
    /// <param name="baseDelay">The base delay for <see cref="QueueRetryPolicy.Fixed"/> and <see cref="QueueRetryPolicy.Exponential"/>.</param>
    /// <param name="dequeueCount">The 1-based dequeue count, including the attempt that just failed.</param>
    /// <param name="schedule">Explicit delays for <see cref="QueueRetryPolicy.Schedule"/>; the last entry repeats.</param>
    /// <returns>The computed delay, capped at <see cref="MaxDelay"/>.</returns>
    public static TimeSpan Compute(QueueRetryPolicy policy, TimeSpan baseDelay, int dequeueCount, IReadOnlyList<TimeSpan>? schedule = null)
    {
        if (policy == QueueRetryPolicy.None)
            return TimeSpan.Zero;

        int retryNumber = Math.Max(0, dequeueCount - 1);

        if (policy == QueueRetryPolicy.Schedule)
        {
            if (schedule is not { Count: > 0 })
                return TimeSpan.Zero;

            var scheduled = schedule[Math.Min(retryNumber, schedule.Count - 1)];
            return scheduled < TimeSpan.Zero ? TimeSpan.Zero : scheduled;
        }

        if (baseDelay <= TimeSpan.Zero)
            return TimeSpan.Zero;

        double delayMs = policy switch
        {
            QueueRetryPolicy.Fixed => baseDelay.TotalMilliseconds,
            QueueRetryPolicy.Exponential => baseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(retryNumber, MaxExponent)),
            _ => 0
        };

        delayMs = Math.Min(delayMs, MaxDelay.TotalMilliseconds);

        // ±10% jitter keeps a burst of failures from retrying in lockstep
        double jitter = (Random.Shared.NextDouble() * 2 - 1) * delayMs * 0.1;
        delayMs = Math.Clamp(delayMs + jitter, 0, MaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(delayMs);
    }

    /// <summary>
    /// Parses a schedule such as <c>"5s,1m,15m,30m"</c>. Accepted units are <c>ms</c>, <c>s</c>, <c>m</c>, <c>h</c>;
    /// a bare number means seconds; <c>hh:mm:ss</c> is accepted as well.
    /// </summary>
    public static IReadOnlyList<TimeSpan> ParseSchedule(string schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
            throw new ArgumentException("Retry schedule cannot be empty.", nameof(schedule));

        var result = new List<TimeSpan>();
        foreach (var raw in schedule.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            result.Add(ParseDuration(raw));

        if (result.Count == 0)
            throw new ArgumentException("Retry schedule cannot be empty.", nameof(schedule));

        return result;
    }

    private static TimeSpan ParseDuration(string token)
    {
        if (TimeSpan.TryParse(token, CultureInfo.InvariantCulture, out var parsed) && token.Contains(':'))
            return parsed;

        var unitStart = token.Length;
        while (unitStart > 0 && char.IsLetter(token[unitStart - 1]))
            unitStart--;

        var number = token[..unitStart];
        var unit = token[unitStart..].ToLowerInvariant();

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new FormatException($"Invalid retry delay '{token}'.");

        return unit switch
        {
            "" or "s" or "sec" or "secs" => TimeSpan.FromSeconds(value),
            "ms" => TimeSpan.FromMilliseconds(value),
            "m" or "min" or "mins" => TimeSpan.FromMinutes(value),
            "h" or "hr" or "hrs" => TimeSpan.FromHours(value),
            _ => throw new FormatException($"Invalid retry delay unit in '{token}'. Use ms, s, m, or h.")
        };
    }
}
