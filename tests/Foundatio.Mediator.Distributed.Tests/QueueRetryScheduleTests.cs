namespace Foundatio.Mediator.Distributed.Tests;

public class QueueRetryScheduleTests
{
    [Fact]
    public void ParseSchedule_AcceptsUnitsAndBareSeconds()
    {
        var schedule = QueueRetryDelay.ParseSchedule("5s, 1m ,15m,30m");
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30)], schedule);

        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500), TimeSpan.FromHours(1), TimeSpan.FromMinutes(2)],
            QueueRetryDelay.ParseSchedule("10;500ms;1h;00:02:00"));

        Assert.Throws<FormatException>(() => QueueRetryDelay.ParseSchedule("5 parsecs"));
        Assert.Throws<ArgumentException>(() => QueueRetryDelay.ParseSchedule(" , "));
    }

    [Fact]
    public void Schedule_UsesEachDelayThenRepeatsTheLast()
    {
        var schedule = QueueRetryDelay.ParseSchedule("5s,1m,15m,30m");

        Assert.Equal(TimeSpan.FromSeconds(5), QueueRetryDelay.Compute(QueueRetryPolicy.Schedule, TimeSpan.Zero, dequeueCount: 1, schedule));
        Assert.Equal(TimeSpan.FromMinutes(1), QueueRetryDelay.Compute(QueueRetryPolicy.Schedule, TimeSpan.Zero, dequeueCount: 2, schedule));
        Assert.Equal(TimeSpan.FromMinutes(15), QueueRetryDelay.Compute(QueueRetryPolicy.Schedule, TimeSpan.Zero, dequeueCount: 3, schedule));
        Assert.Equal(TimeSpan.FromMinutes(30), QueueRetryDelay.Compute(QueueRetryPolicy.Schedule, TimeSpan.Zero, dequeueCount: 4, schedule));
        Assert.Equal(TimeSpan.FromMinutes(30), QueueRetryDelay.Compute(QueueRetryPolicy.Schedule, TimeSpan.Zero, dequeueCount: 12, schedule));

        Assert.Equal(TimeSpan.Zero, QueueRetryDelay.Compute(QueueRetryPolicy.Schedule, TimeSpan.Zero, dequeueCount: 1, schedule: null));
    }

    [Fact]
    public void Exponential_IsCappedAndNeverOverflows()
    {
        var delay = QueueRetryDelay.Compute(QueueRetryPolicy.Exponential, TimeSpan.FromSeconds(5), dequeueCount: 5000);
        Assert.InRange(delay, TimeSpan.Zero, QueueRetryDelay.MaxDelay);

        var first = QueueRetryDelay.Compute(QueueRetryPolicy.Exponential, TimeSpan.FromSeconds(10), dequeueCount: 1);
        Assert.InRange(first, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(11));
    }
}
