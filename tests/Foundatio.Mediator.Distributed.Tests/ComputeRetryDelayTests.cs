using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Mediator.Distributed.Tests;

public class ComputeRetryDelayTests
{
    private static QueueWorker CreateWorker(QueueRetryPolicy policy, TimeSpan baseDelay)
    {
        var options = new QueueWorkerOptions
        {
            QueueName = "test",
            MessageType = typeof(object),
            Registration = null!,
            RetryPolicy = policy,
            RetryDelay = baseDelay
        };

        return new QueueWorker(
            new InMemoryQueueClient(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            options,
            null,
            NullLogger<QueueWorker>.Instance);
    }

    [Fact]
    public void None_ReturnsZero()
    {
        var worker = CreateWorker(QueueRetryPolicy.None, TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.Zero, worker.ComputeRetryDelay(1));
        Assert.Equal(TimeSpan.Zero, worker.ComputeRetryDelay(5));
    }

    [Fact]
    public void ZeroBaseDelay_ReturnsZero()
    {
        var worker = CreateWorker(QueueRetryPolicy.Exponential, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, worker.ComputeRetryDelay(3));
    }

    [Fact]
    public void Fixed_ReturnsSameDelayWithinJitterBounds()
    {
        var baseDelay = TimeSpan.FromSeconds(10);
        var worker = CreateWorker(QueueRetryPolicy.Fixed, baseDelay);

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            var delay = worker.ComputeRetryDelay(attempt);
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
        var worker = CreateWorker(QueueRetryPolicy.Exponential, baseDelay);

        // dequeueCount=1 → retryNumber=0 → 5s * 2^0 = 5s
        var delay1 = worker.ComputeRetryDelay(1);
        Assert.InRange(delay1.TotalSeconds, 4.5, 5.5);

        // dequeueCount=2 → retryNumber=1 → 5s * 2^1 = 10s
        var delay2 = worker.ComputeRetryDelay(2);
        Assert.InRange(delay2.TotalSeconds, 9.0, 11.0);

        // dequeueCount=3 → retryNumber=2 → 5s * 2^2 = 20s
        var delay3 = worker.ComputeRetryDelay(3);
        Assert.InRange(delay3.TotalSeconds, 18.0, 22.0);

        // dequeueCount=4 → retryNumber=3 → 5s * 2^3 = 40s
        var delay4 = worker.ComputeRetryDelay(4);
        Assert.InRange(delay4.TotalSeconds, 36.0, 44.0);
    }

    [Fact]
    public void Exponential_CapsAt15Minutes()
    {
        var baseDelay = TimeSpan.FromSeconds(5);
        var worker = CreateWorker(QueueRetryPolicy.Exponential, baseDelay);

        // dequeueCount=20 → retryNumber=19 → 5s * 2^19 = 2,621,440s (way over 15min)
        var delay = worker.ComputeRetryDelay(20);
        Assert.True(delay <= TimeSpan.FromMinutes(15),
            $"Expected <= 15 minutes but got {delay}");
        // Should be at the cap (within jitter)
        Assert.InRange(delay.TotalMinutes, 13.5, 15.0);
    }

    [Fact]
    public void JitterIsProportional()
    {
        var baseDelay = TimeSpan.FromSeconds(10);
        var worker = CreateWorker(QueueRetryPolicy.Fixed, baseDelay);

        // Run many iterations to verify jitter stays within ±10%
        var delays = Enumerable.Range(0, 100)
            .Select(_ => worker.ComputeRetryDelay(1).TotalMilliseconds)
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
