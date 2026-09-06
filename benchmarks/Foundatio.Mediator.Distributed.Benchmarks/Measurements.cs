using System.Diagnostics;

namespace DistributedBenchmarks;

public sealed record Distribution(double P50Ms, double P95Ms, double P99Ms, double MaxMs)
{
    public static Distribution From(IEnumerable<long> ticks)
    {
        var sorted = ticks.Order().ToArray();
        if (sorted.Length == 0) return new(0, 0, 0, 0);
        double At(double percentile) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * percentile) - 1)] * 1000d / Stopwatch.Frequency;
        return new(At(.50), At(.95), At(.99), At(1));
    }
}

public sealed record ProcessCost(long AllocatedBytes, double CpuMilliseconds, int Gen0, int Gen1, int Gen2, long WorkingSetBytes)
{
    public static ProcessCost Read()
    {
        using var process = Process.GetCurrentProcess();
        return new(GC.GetTotalAllocatedBytes(false), process.TotalProcessorTime.TotalMilliseconds, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), process.WorkingSet64);
    }
    public ProcessCost Since(ProcessCost before) => new(AllocatedBytes - before.AllocatedBytes, CpuMilliseconds - before.CpuMilliseconds, Gen0 - before.Gen0, Gen1 - before.Gen1, Gen2 - before.Gen2, WorkingSetBytes);
}

public sealed record DeliverySnapshot(int Received, int Duplicates, int Invalid, long LastTimestamp, long[]? LatencyTicks = null, long[]? ScheduledLatencyTicks = null, ProcessCost? Cost = null);

public sealed class DeliveryCollector(Settings settings)
{
    private readonly Phase _warmup = new(settings.Warmup);
    private readonly Phase _measured = new(settings.Count);
    private ProcessCost _before = ProcessCost.Read();
    public void Begin() => _before = ProcessCost.Read();

    public void Complete(BenchmarkMessage message)
    {
        var phase = message.Warmup ? _warmup : _measured;
        if (message.RunId != settings.RunId || message.Sequence < 0 || message.Sequence >= phase.Seen.Length || message.Payload.Length != settings.PayloadBytes || message.Payload[0] != 'x' || message.Payload[^1] != 'x')
        {
            Interlocked.Increment(ref phase.Invalid);
            return;
        }
        if (Interlocked.Exchange(ref phase.Seen[message.Sequence], 1) != 0)
        {
            Interlocked.Increment(ref phase.Duplicates);
            return;
        }
        if (settings.WorkMicroseconds > 0)
        {
            long finish = Stopwatch.GetTimestamp() + settings.WorkMicroseconds * Stopwatch.Frequency / 1_000_000;
            while (Stopwatch.GetTimestamp() < finish) Thread.SpinWait(4);
        }
        long now = Stopwatch.GetTimestamp();
        phase.Latency[message.Sequence] = now - message.Started;
        phase.ScheduledLatency[message.Sequence] = now - message.Scheduled;
        long old;
        do { old = Volatile.Read(ref phase.Last); }
        while (old < now && Interlocked.CompareExchange(ref phase.Last, now, old) != old);
        Interlocked.Increment(ref phase.Received);
    }

    public DeliverySnapshot Snapshot(bool warmup, bool details = false)
    {
        var phase = warmup ? _warmup : _measured;
        return new(Volatile.Read(ref phase.Received), Volatile.Read(ref phase.Duplicates), Volatile.Read(ref phase.Invalid), Volatile.Read(ref phase.Last),
            details ? phase.Latency.Where(t => t > 0).ToArray() : null,
            details ? phase.ScheduledLatency.Where(t => t > 0).ToArray() : null,
            details ? ProcessCost.Read().Since(_before) : null);
    }

    private sealed class Phase(int count)
    {
        public readonly int[] Seen = new int[count];
        public readonly long[] Latency = new long[count];
        public readonly long[] ScheduledLatency = new long[count];
        public int Received, Duplicates, Invalid;
        public long Last;
    }
}

public sealed record RunResult
{
    public required Settings Settings { get; init; }
    public required string Runtime { get; init; }
    public required string MassTransitVersion { get; init; }
    public required string FoundatioVersion { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required bool Valid { get; init; }
    public string? Error { get; init; }
    public int Sent { get; init; }
    public int ExpectedDeliveries { get; init; }
    public int Delivered { get; init; }
    public int Missing { get; init; }
    public int Duplicates { get; init; }
    public int InvalidMessages { get; init; }
    public long Dropped { get; init; }
    public long TransportPublished { get; init; }
    public int TransportFailures { get; init; }
    public double ApiSeconds { get; init; }
    public double DeliverySeconds { get; init; }
    public double DrainSeconds { get; init; }
    public double MessagesPerSecond { get; init; }
    public double DeliveriesPerSecond { get; init; }
    public Distribution ApiLatency { get; init; } = new(0, 0, 0, 0);
    public Distribution DeliveryLatency { get; init; } = new(0, 0, 0, 0);
    public Distribution ScheduledDeliveryLatency { get; init; } = new(0, 0, 0, 0);
    public ProcessCost[] Processes { get; init; } = [];
    public int[] SubscriberDeliveries { get; init; } = [];
}
