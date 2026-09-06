using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Mediator.Distributed.Tests;

public class QueueLeaseOwnershipTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("abandon")]
    [InlineData("renew")]
    [InlineData("dead-letter")]
    public async Task ExpiredReceipt_CannotSettleOrExtendANewerDelivery(string operation)
    {
        var time = new FakeTimeProvider();
        await using var client = new InMemoryQueueClient(time);
        await client.SendAsync("work", [new QueueEntry { Body = "{}"u8.ToArray() }], TestContext.Current.CancellationToken);
        var first = Assert.Single(await client.ReceiveAsync("work", 1, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        time.Advance(TimeSpan.FromSeconds(11));
        var second = Assert.Single(await client.ReceiveAsync("work", 1, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<QueueLeaseLostException>(() => operation switch
        {
            "complete" => client.CompleteAsync(first, TestContext.Current.CancellationToken),
            "abandon" => client.AbandonAsync(first, cancellationToken: TestContext.Current.CancellationToken),
            "renew" => client.RenewTimeoutAsync(first, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken),
            _ => client.DeadLetterAsync(first, "stale worker", TestContext.Current.CancellationToken)
        });

        Assert.Equal(1, client.GetInFlightCount("work"));
        Assert.Equal(0, client.GetDeadLetterCount("work"));
        time.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(1, client.GetPendingCount("work"));
        Assert.Equal(2, second.DequeueCount);
    }

    [Fact]
    public async Task DelayedAbandon_RemainsVisibleInQueueStatistics()
    {
        var time = new FakeTimeProvider();
        await using var client = new InMemoryQueueClient(time);
        await client.SendAsync("work", [new QueueEntry { Body = "{}"u8.ToArray() }], TestContext.Current.CancellationToken);
        var message = Assert.Single(await client.ReceiveAsync("work", 1, TestContext.Current.CancellationToken));
        await client.AbandonAsync(message, TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        var stats = Assert.Single(await client.GetQueueStatsAsync(["work"], TestContext.Current.CancellationToken));
        Assert.Equal(1, stats.DelayedCount);
        Assert.Equal(0, stats.ActiveCount);
        Assert.Equal(0, stats.InFlightCount);

        time.Advance(TimeSpan.FromHours(1));
        stats = Assert.Single(await client.GetQueueStatsAsync(["work"], TestContext.Current.CancellationToken));
        Assert.Equal(0, stats.DelayedCount);
        Assert.Equal(1, stats.ActiveCount);
    }
}
