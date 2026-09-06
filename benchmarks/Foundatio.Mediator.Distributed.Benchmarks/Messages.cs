using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using MassTransit;

namespace DistributedBenchmarks;

public record BenchmarkMessage(string RunId, int Sequence, bool Warmup, long Started, long Scheduled, string Payload);
public sealed record QueueMessage(string RunId, int Sequence, bool Warmup, long Started, long Scheduled, string Payload)
    : BenchmarkMessage(RunId, Sequence, Warmup, Started, Scheduled, Payload);
public sealed record LocalMessage(string RunId, int Sequence, bool Warmup, long Started, long Scheduled, string Payload)
    : BenchmarkMessage(RunId, Sequence, Warmup, Started, Scheduled, Payload);
public sealed record BenchmarkEvent(string RunId, int Sequence, bool Warmup, long Started, long Scheduled, string Payload)
    : BenchmarkMessage(RunId, Sequence, Warmup, Started, Scheduled, Payload), IDistributedNotification;

[Queue(QueueName = "work", Concurrency = 8, PrefetchCount = 8)]
public static class WorkHandler
{
    public static Result Handle(QueueMessage message, DeliveryCollector collector)
    {
        collector.Complete(message);
        return Result.Ok();
    }
}

public static class LocalHandler
{
    public static Result Handle(LocalMessage message, DeliveryCollector collector)
    {
        collector.Complete(message);
        return Result.Ok();
    }
}

public static class EventHandler
{
    public static void Handle(BenchmarkEvent message, DeliveryCollector collector) => collector.Complete(message);
}

[FoundatioIgnore]
public sealed class QueueConsumer(DeliveryCollector collector) : IConsumer<QueueMessage>
{
    public Task Consume(ConsumeContext<QueueMessage> context)
    {
        collector.Complete(context.Message);
        return Task.CompletedTask;
    }
}

[FoundatioIgnore]
public sealed class EventConsumer(DeliveryCollector collector) : IConsumer<BenchmarkEvent>
{
    public Task Consume(ConsumeContext<BenchmarkEvent> context)
    {
        collector.Complete(context.Message);
        return Task.CompletedTask;
    }
}
