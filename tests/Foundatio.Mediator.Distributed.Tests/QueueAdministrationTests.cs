using System.Text;
using Foundatio.Mediator.Distributed.Testing;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Distributed.Tests;

public class QueueAdministrationTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private static ServiceProvider BuildHost(InMemoryTransport transport, HandlerSignal signal, Action<DistributedQueueOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddInMemoryDistributedTransport(transport);
        services.AddMediator(b => b.AddAssembly<PoisonBodyMessageHandler>().AddAssembly<QueueAdministrationHandler>())
            .AddDistributedQueues(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Overview_PeekReplayAndPurgeDeadLetters()
    {
        await using var transport = new InMemoryTransport();
        var signal = new HandlerSignal();
        await using var provider = BuildHost(transport, signal, o => o.Workers = WorkerSelection.Only("PoisonBodyMessage"));
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hosted = await provider.StartHostedServicesAsync(cts.Token);
        try
        {
            await transport.Queues.SendAsync("PoisonBodyMessage", [new QueueEntry
            {
                Body = Encoding.UTF8.GetBytes("not json at all"),
                Headers = new Dictionary<string, string>
                {
                    [MessageHeaders.MessageType] = typeof(PoisonBodyMessage).FullName!,
                    [MessageHeaders.CorrelationId] = "corr-1"
                }
            }], cts.Token);

            await WaitForDeadLettersAsync(transport, "PoisonBodyMessage", 1, cts.Token);

            var overview = await mediator.InvokeAsync<Result<IReadOnlyList<QueueOverview>>>(new GetQueueOverview(), cts.Token);
            Assert.True(overview.IsSuccess);
            var poison = Assert.Single(overview.Value!, q => q.QueueName == "PoisonBodyMessage");
            Assert.True(poison.WorkerRunsHere);
            Assert.Equal(1, poison.DeadLetterCount);
            Assert.Contains(nameof(PoisonBodyMessageHandler), poison.Handlers);
            Assert.Contains(overview.Value!, q => q.QueueName == "QueuedCommand" && !q.WorkerRunsHere && q.IsRunning is null);

            var detail = await mediator.InvokeAsync<Result<QueueOverview>>(new GetQueueDetail("PoisonBodyMessage"), cts.Token);
            Assert.Equal(3, detail.Value!.MaxAttempts);
            var missing = await mediator.InvokeAsync<Result<QueueOverview>>(new GetQueueDetail("nope"), cts.Token);
            Assert.Equal(ResultStatus.NotFound, missing.Status);

            // Peek leaves the dead letter where it is
            var peeked = await mediator.InvokeAsync<Result<IReadOnlyList<DeadLetterView>>>(new ListDeadLetters("PoisonBodyMessage"), cts.Token);
            var view = Assert.Single(peeked.Value!);
            Assert.Contains("Deserialization failed", view.Reason);
            Assert.Equal("PoisonBodyMessage", view.OriginalQueueName);
            Assert.Equal("corr-1", view.CorrelationId);
            Assert.Equal("not json at all", view.Body);
            Assert.Equal(1, view.Attempts);
            Assert.Equal(1, transport.Queues.Inner is InMemoryQueueClient q1 ? q1.GetDeadLetterCount("PoisonBodyMessage") : -1);

            // Replay sends it back; it is still poison, so it dead-letters again with a replay marker
            var replayed = await mediator.InvokeAsync<Result<DeadLetterReplayResult>>(new ReplayDeadLetters("PoisonBodyMessage"), cts.Token);
            Assert.Equal(1, replayed.Value!.Replayed);
            await WaitForDeadLettersAsync(transport, "PoisonBodyMessage", 1, cts.Token);

            var again = await mediator.InvokeAsync<Result<IReadOnlyList<DeadLetterView>>>(new ListDeadLetters("PoisonBodyMessage"), cts.Token);
            Assert.Single(again.Value!);

            var inMemory = (InMemoryQueueClient)transport.Queues.Inner;
            var raw = inMemory.DrainDeadLetterMessages("PoisonBodyMessage");
            Assert.True(raw[0].Headers.ContainsKey(MessageHeaders.ReplayedAt));
            await transport.Queues.SendAsync(QueueDefinition.DeadLetterQueueNameFor("PoisonBodyMessage"), [new QueueEntry { Body = raw[0].Body, Headers = new Dictionary<string, string>(raw[0].Headers) }], cts.Token);

            var purged = await mediator.InvokeAsync<Result<DeadLetterPurgeResult>>(new PurgeDeadLetters("PoisonBodyMessage"), cts.Token);
            Assert.Equal(1, purged.Value!.Purged);
            Assert.Equal(0, inMemory.GetDeadLetterCount("PoisonBodyMessage"));

            Assert.Empty(signal.Values);
        }
        finally
        {
            await hosted.StopAllAsync();
        }
    }

    [Fact]
    public async Task Jobs_ListGetAndCancelThroughTheStore()
    {
        await using var transport = new InMemoryTransport();
        var signal = new HandlerSignal();
        await using var provider = BuildHost(transport, signal, o =>
        {
            o.Workers = WorkerSelection.None;
            o.AllowInMemoryWithoutWorkers = true;
            o.JobMetadataProvider = m => m is MetadataTrackedCommand c ? new Dictionary<string, string> { ["tenant"] = c.Tenant } : null;
        });
        var mediator = provider.GetRequiredService<IMediator>();

        var accepted = await mediator.InvokeAsync<Result>(new MetadataTrackedCommand("tracked", "acme"), TestCancellationToken);
        Assert.Equal(ResultStatus.Accepted, accepted.Status);
        var jobId = accepted.Location!;

        var queued = await mediator.InvokeAsync<Result<IReadOnlyList<QueueJobState>>>(new ListQueueJobs("MetadataTrackedCommand", QueueJobStatus.Queued), TestCancellationToken);
        Assert.Equal(jobId, Assert.Single(queued.Value!).JobId);

        var job = await mediator.InvokeAsync<Result<QueueJobState>>(new GetQueueJob(jobId), TestCancellationToken);
        Assert.Equal("acme", job.Value!.Metadata!["tenant"]);

        var cancel = await mediator.InvokeAsync<Result<QueueJobCancellation>>(new CancelQueueJob(jobId), TestCancellationToken);
        Assert.True(cancel.Value!.CancellationRequested);

        var unknown = await mediator.InvokeAsync<Result<QueueJobState>>(new GetQueueJob("missing"), TestCancellationToken);
        Assert.Equal(ResultStatus.NotFound, unknown.Status);

        var sent = Assert.Single(transport.Queues.Sent<MetadataTrackedCommand>());
        Assert.Equal("tracked", sent.Value);
        Assert.Equal(jobId, transport.Queues.SentMessages[0].JobId);
    }

    [Fact]
    public async Task RecordingClient_RecordsSendsAndDrains()
    {
        await using var transport = new InMemoryTransport();
        var signal = new HandlerSignal();
        await using var provider = BuildHost(transport, signal, o => o.Workers = WorkerSelection.Only("QueuedCommand"));
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hosted = await provider.StartHostedServicesAsync(cts.Token);
        try
        {
            for (int i = 0; i < 3; i++)
                await mediator.InvokeAsync(new QueuedCommand($"c{i}"), cts.Token);

            Assert.True(transport.Queues.WasSent<QueuedCommand>());
            Assert.Equal(["c0", "c1", "c2"], transport.Queues.Sent<QueuedCommand>().Select(c => c.Value));
            Assert.All(transport.Queues.SentMessages, s => Assert.Equal("QueuedCommand", s.QueueName));

            await transport.DrainAsync(TimeSpan.FromSeconds(10), cts.Token);
            Assert.Equal(3, signal.Values.Count);

            transport.Queues.Clear();
            Assert.Empty(transport.Queues.SentMessages);
        }
        finally
        {
            await hosted.StopAllAsync();
        }
    }

    private static async Task WaitForDeadLettersAsync(InMemoryTransport transport, string queueName, int expected, CancellationToken ct)
    {
        var inMemory = (InMemoryQueueClient)transport.Queues.Inner;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (inMemory.GetDeadLetterCount(queueName) < expected && DateTime.UtcNow < deadline)
            await Task.Delay(25, ct);
        Assert.Equal(expected, inMemory.GetDeadLetterCount(queueName));
    }
}
