using Foundatio.Mediator.Distributed;

namespace Foundatio.Mediator.Distributed.Tests;

public class ComputeRetryDelayTests
{
    [Fact]
    public void None_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, QueueRetryDelay.Compute(QueueRetryPolicy.None, TimeSpan.FromSeconds(5), 1));
        Assert.Equal(TimeSpan.Zero, QueueRetryDelay.Compute(QueueRetryPolicy.None, TimeSpan.FromSeconds(5), 5));
    }

    [Fact]
    public void ZeroBaseDelay_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, QueueRetryDelay.Compute(QueueRetryPolicy.Exponential, TimeSpan.Zero, 3));
    }

    [Fact]
    public void Fixed_ReturnsSameDelayWithinJitterBounds()
    {
        var baseDelay = TimeSpan.FromSeconds(10);

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            var delay = QueueRetryDelay.Compute(QueueRetryPolicy.Fixed, baseDelay, attempt);
            // Fixed: always ~baseDelay ±10% jitter
            Assert.InRange(delay.TotalMilliseconds,
                baseDelay.TotalMilliseconds * 0.9,
                baseDelay.TotalMilliseconds * 1.1);
        }
    }

    [Fact]
    public void Exponential_DoublesEachRetry()
    {
        var baseDelay = TimeSpan.FromSeconds(5);

        // dequeueCount=1 → retryNumber=0 → 5s * 2^0 = 5s
        var delay1 = QueueRetryDelay.Compute(QueueRetryPolicy.Exponential, baseDelay, 1);
        Assert.InRange(delay1.TotalSeconds, 4.5, 5.5);

        // dequeueCount=2 → retryNumber=1 → 5s * 2^1 = 10s
        var delay2 = QueueRetryDelay.Compute(QueueRetryPolicy.Exponential, baseDelay, 2);
        Assert.InRange(delay2.TotalSeconds, 9.0, 11.0);

        // dequeueCount=3 → retryNumber=2 → 5s * 2^2 = 20s
        var delay3 = QueueRetryDelay.Compute(QueueRetryPolicy.Exponential, baseDelay, 3);
        Assert.InRange(delay3.TotalSeconds, 18.0, 22.0);

        // dequeueCount=4 → retryNumber=3 → 5s * 2^3 = 40s
        var delay4 = QueueRetryDelay.Compute(QueueRetryPolicy.Exponential, baseDelay, 4);
        Assert.InRange(delay4.TotalSeconds, 36.0, 44.0);
    }

    [Fact]
    public void Exponential_CapsAt15Minutes()
    {
        var baseDelay = TimeSpan.FromSeconds(5);

        // dequeueCount=20 → retryNumber=19 → 5s * 2^19 = 2,621,440s (way over 15min)
        var delay = QueueRetryDelay.Compute(QueueRetryPolicy.Exponential, baseDelay, 20);
        Assert.True(delay <= TimeSpan.FromMinutes(15),
            $"Expected <= 15 minutes but got {delay}");
        // Should be at the cap (within jitter)
        Assert.InRange(delay.TotalMinutes, 13.5, 15.0);
    }

    [Fact]
    public void JitterIsProportional()
    {
        var baseDelay = TimeSpan.FromSeconds(10);

        // Run many iterations to verify jitter stays within ±10%
        var delays = Enumerable.Range(0, 100)
            .Select(_ => QueueRetryDelay.Compute(QueueRetryPolicy.Fixed, baseDelay, 1).TotalMilliseconds)
            .ToList();

        var min = delays.Min();
        var max = delays.Max();

        Assert.True(min >= baseDelay.TotalMilliseconds * 0.9,
            $"Min delay {min}ms is below 90% of base ({baseDelay.TotalMilliseconds * 0.9}ms)");
        Assert.True(max <= baseDelay.TotalMilliseconds * 1.1,
            $"Max delay {max}ms is above 110% of base ({baseDelay.TotalMilliseconds * 1.1}ms)");

        // Verify there IS some variance (not all identical)
        Assert.True(max - min > 1, "Expected jitter to produce some variance");
    }
}
