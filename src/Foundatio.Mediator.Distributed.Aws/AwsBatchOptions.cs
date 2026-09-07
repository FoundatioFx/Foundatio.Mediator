namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>Bounds transport coalescing while preserving a broker result for every caller.</summary>
public sealed class AwsBatchOptions
{
    /// <summary>Maximum entries per request, from 1 to the AWS limit of 10. Default is 10.</summary>
    public int MaxBatchSize { get; set; } = 10;

    /// <summary>Maximum time to collect a partial batch. Full batches dispatch immediately. Default is 1 millisecond.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMilliseconds(1);

    /// <summary>Maximum simultaneous requests per destination and operation. Default is 4.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Queued entries per destination and operation before callers wait for capacity. Default is 1,024.</summary>
    public int Capacity { get; set; } = 1024;

    /// <summary>Maximum duration of an AWS batch request, including SDK retries. Default is 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal AwsBatchOptions Snapshot()
    {
        if (MaxBatchSize is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(MaxBatchSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Capacity, 1);
        if (MaxDelay < TimeSpan.Zero || MaxDelay > TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(MaxDelay));
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
        return new AwsBatchOptions { MaxBatchSize = MaxBatchSize, MaxDelay = MaxDelay, MaxConcurrency = MaxConcurrency, Capacity = Capacity, RequestTimeout = RequestTimeout };
    }
}
