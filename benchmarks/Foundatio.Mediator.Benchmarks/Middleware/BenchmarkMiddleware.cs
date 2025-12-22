using Foundatio.Mediator;
using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using System.Diagnostics;

namespace Foundatio.Mediator.Benchmarks.Middleware;

/// <summary>
/// Simple timing middleware for benchmarking - simulates real-world logging/timing middleware.
/// Only applies to GetOrderWithDependencies (FullQuery benchmark).
/// </summary>
public class TimingMiddleware
{
    public Stopwatch Before(GetOrderWithDependencies message, HandlerExecutionInfo info)
    {
        return Stopwatch.StartNew();
    }

    public void Finally(GetOrderWithDependencies message, Stopwatch? stopwatch, HandlerExecutionInfo info)
    {
        stopwatch?.Stop();
        // In real middleware, you'd log here - we just stop the timer for the benchmark
    }
}

/// <summary>
/// Short-circuit middleware that immediately returns a cached result without calling the handler.
/// This demonstrates middleware returning early (cache hit, validation success with cached result, etc.)
/// </summary>
public class ShortCircuitMiddleware
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    public HandlerResult Before(GetCachedOrder message)
    {
        // Always short-circuit with cached result - simulates cache hit scenario
        return HandlerResult.ShortCircuit(_cachedOrder);
    }
}
