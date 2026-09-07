using System.Text;
using Foundatio.Mediator.Distributed.Testing;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
            Assert.False(view.BodyTruncated);
            Assert.Equal("corr-1", view.Headers[MessageHeaders.CorrelationId]);
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

    [Theory]
    [InlineData(null, "Tracked work")]
    [InlineData("  Customer exports  ", "Customer exports")]
    [InlineData("   ", null)]
    public async Task DisplayName_ExposesMetadata_WithoutChangingQueueIdentity(string? configuredLabel, string? expectedLabel)
    {
        await using var transport = new InMemoryTransport();
        await using var provider = BuildHost(transport, new HandlerSignal(), options =>
        {
            options.ResourcePrefix = "display-test";
            options.Workers = WorkerSelection.Only("MetadataTrackedCommand");
            // Equal labels must not combine independent subscriptions.
            options.QueueOverrides["QueuedCommand"] = settings => settings.DisplayName = configuredLabel ?? "Tracked work";
            if (configuredLabel is not null)
                options.QueueOverrides["MetadataTrackedCommand"] = settings => settings.DisplayName = configuredLabel;
        });
        const string queueName = "display-test-MetadataTrackedCommand";
        var topology = provider.GetRequiredService<QueueTopology>();
        var registration = topology.GetByQueueName(queueName)!;
        Assert.True(registration.WorkerRunsHere);
        Assert.Equal(expectedLabel, registration.DisplayName);
        Assert.Single(registration.Handlers);
        Assert.Single(topology.GetByQueueName("display-test-QueuedCommand")!.Handlers);
        // A shared queue can declare its label on one member without repeating it on every handler.
        Assert.Equal("Shared events", topology.GetByQueueName("display-test-shared-queue-events")!.DisplayName);
        Assert.Equal(expectedLabel, provider.GetRequiredService<IQueueWorkerRegistry>().GetWorker(queueName)!.DisplayName);

        var mediator = provider.GetRequiredService<IMediator>();
        var overview = await mediator.InvokeAsync<Result<IReadOnlyList<QueueOverview>>>(new GetQueueOverview(), TestCancellationToken);
        Assert.True(overview.IsSuccess);
        Assert.Equal(expectedLabel, Assert.Single(overview.Value!, queue => queue.QueueName == queueName).DisplayName);
        var detail = await mediator.InvokeAsync<Result<QueueOverview>>(new GetQueueDetail(queueName), TestCancellationToken);
        Assert.Equal(expectedLabel, detail.Value!.DisplayName);
        Assert.Equal(queueName, detail.Value.QueueName);
        if (expectedLabel is not null)
        {
            var byLabel = await mediator.InvokeAsync<Result<QueueOverview>>(new GetQueueDetail(expectedLabel), TestCancellationToken);
            Assert.Equal(ResultStatus.NotFound, byLabel.Status);
        }

        var accepted = await mediator.EnqueueAsync(new MetadataTrackedCommand("export", "tenant"), TestCancellationToken);
        Assert.Equal(queueName, accepted.Value.QueueName);
        Assert.Equal(queueName, Assert.Single(transport.Queues.SentMessages).QueueName);
        var job = await provider.GetRequiredService<IQueueJobStateStore>().GetJobStateAsync(accepted.Value.JobId!, TestCancellationToken);
        Assert.Equal(queueName, job!.QueueName);
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

        var accepted = await mediator.EnqueueAsync(new MetadataTrackedCommand("tracked", "acme"), TestCancellationToken);
        Assert.Equal(ResultStatus.Accepted, accepted.Status);
        var jobId = accepted.Value.JobId!;

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

    [Fact]
    public async Task TrackedReplay_CreatesNewJob_PreservesOriginalFailure_AndExecutes()
    {
        await using var transport = new InMemoryTransport();
        var signal = new HandlerSignal();
        await using var provider = BuildHost(transport, signal, options => options.Workers = WorkerSelection.Only("MetadataTrackedCommand"));
        var store = provider.GetRequiredService<IQueueJobStateStore>();
        await store.SetJobStateAsync(new QueueJobState
        {
            JobId = "original", QueueName = "MetadataTrackedCommand", Status = QueueJobStatus.Failed,
            Attempt = 3, ErrorMessage = "Old failure", CreatedUtc = DateTimeOffset.UtcNow.AddHours(-1)
        }, cancellationToken: TestCancellationToken);
        await transport.Queues.SendAsync(QueueDefinition.DeadLetterQueueNameFor("MetadataTrackedCommand"), [new QueueEntry
        {
            Body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new MetadataTrackedCommand("replayed", "tenant")),
            Headers = new Dictionary<string, string>
            {
                [MessageHeaders.JobId] = "original",
                [MessageHeaders.OriginalQueueName] = "MetadataTrackedCommand",
                [MessageHeaders.MessageType] = typeof(MetadataTrackedCommand).FullName!,
                [MessageHeaders.DeadLetteredAt] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")
            }
        }], TestCancellationToken);
        var replay = await provider.GetRequiredService<IMediator>().InvokeAsync<Result<DeadLetterReplayResult>>(
            new ReplayDeadLetters("MetadataTrackedCommand", Max: 1), TestCancellationToken);
        Assert.Equal(1, replay.Value.Replayed);
        Assert.Equal(QueueJobStatus.Failed, (await store.GetJobStateAsync("original", TestCancellationToken))!.Status);
        var newJob = Assert.Single(await store.GetJobsByStatusAsync("MetadataTrackedCommand", QueueJobStatus.Queued, cancellationToken: TestCancellationToken));
        Assert.NotEqual("original", newJob.JobId);
        Assert.Equal(new QueueReceipt("MetadataTrackedCommand", newJob.JobId), Assert.Single(replay.Value.Receipts));
        var hosted = await provider.StartHostedServicesAsync(TestCancellationToken);
        try
        {
            await transport.DrainAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
            Assert.Equal(["replayed"], signal.Values);
            Assert.Equal(QueueJobStatus.Completed, (await store.GetJobStateAsync(newJob.JobId, TestCancellationToken))!.Status);
        }
        finally { await hosted.StopAllAsync(); }
    }

    [Fact]
    public async Task TargetedReplay_ReleasesOtherMessagesInReceivedBatch()
    {
        await using var transport = new InMemoryTransport();
        await using var provider = BuildHost(transport, new HandlerSignal());
        var mediator = provider.GetRequiredService<IMediator>();
        const string queue = "PoisonBodyMessage";
        await transport.Queues.SendAsync(QueueDefinition.DeadLetterQueueNameFor(queue), Enumerable.Range(1, 3)
            .Select(i => new QueueEntry
            {
                Body = Encoding.UTF8.GetBytes(new string('x', 5000)),
                Headers = new Dictionary<string, string> { [MessageHeaders.OriginalQueueName] = queue }
            }).ToArray(), TestCancellationToken);
        var peek = await mediator.InvokeAsync<Result<IReadOnlyList<DeadLetterView>>>(new ListDeadLetters(queue, 3), TestCancellationToken);
        Assert.All(peek.Value, view => { Assert.True(view.BodyTruncated); Assert.Equal(4096, view.Body.Length); });
        var target = peek.Value[0].MessageId;

        var replay = await mediator.InvokeAsync<Result<DeadLetterReplayResult>>(
            new ReplayDeadLetters(queue, MessageId: target), TestCancellationToken);

        Assert.Equal(1, replay.Value.Replayed);
        Assert.Equal(new QueueReceipt(queue, null), Assert.Single(replay.Value.Receipts));
        var remaining = await mediator.InvokeAsync<Result<IReadOnlyList<DeadLetterView>>>(new ListDeadLetters(queue, 2), TestCancellationToken);
        Assert.Equal(2, remaining.Value.Count);
        Assert.DoesNotContain(remaining.Value, message => message.MessageId == target);
    }

    [Fact]
    public async Task Overview_TransportFailure_ReportsStatisticsUnavailable()
    {
        await using var transport = new InMemoryTransport();
        await using var provider = BuildHost(transport, new HandlerSignal());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestCancellationToken);
        var handler = new QueueAdministrationHandler(provider.GetRequiredService<QueueTopology>(),
            provider.GetRequiredService<IQueueWorkerRegistry>(), new CancelAfterReceiveClient(cancellation),
            NullLogger<QueueAdministrationHandler>.Instance);
        var result = await handler.HandleAsync(new GetQueueOverview(), TestCancellationToken);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value);
        Assert.All(result.Value, queue => Assert.False(queue.StatisticsAvailable));
    }

    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(11, 100, 1)]
    [InlineData(24, 100, 1)]
    [InlineData(-1, 100, 0)]
    [InlineData(24, 10, 0)]
    public async Task TargetedPurge_DeletesOnlyMatchWithinScanLimit_AndReleasesOtherMessages(int targetIndex, int max, int expectedPurged)
    {
        await using var transport = new InMemoryTransport();
        await using var provider = BuildHost(transport, new HandlerSignal());
        var mediator = provider.GetRequiredService<IMediator>();
        const string queue = "PoisonBodyMessage";
        await transport.Queues.SendAsync(QueueDefinition.DeadLetterQueueNameFor(queue), Enumerable.Range(1, 25)
            .Select(i => new QueueEntry
            {
                Body = Encoding.UTF8.GetBytes($"failed message {i}"),
                Headers = new Dictionary<string, string> { [MessageHeaders.OriginalQueueName] = queue }
            }).ToArray(), TestCancellationToken);
        var before = await mediator.InvokeAsync<Result<IReadOnlyList<DeadLetterView>>>(new ListDeadLetters(queue, 25), TestCancellationToken);
        var target = targetIndex < 0 ? "missing" : before.Value[targetIndex].MessageId;

        var purge = await mediator.InvokeAsync<Result<DeadLetterPurgeResult>>(
            new PurgeDeadLetters(queue, max, target), TestCancellationToken);

        Assert.Equal(expectedPurged, purge.Value.Purged);
        var after = await mediator.InvokeAsync<Result<IReadOnlyList<DeadLetterView>>>(new ListDeadLetters(queue, 25), TestCancellationToken);
        var expected = before.Value.Where(m => expectedPurged == 0 || m.MessageId != target).OrderBy(m => m.MessageId).ToArray();
        var remaining = after.Value.OrderBy(m => m.MessageId).ToArray();
        Assert.Equal(expected.Length, remaining.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].MessageId, remaining[i].MessageId);
            Assert.Equal(expected[i].Body, remaining[i].Body);
            Assert.Equal(expected[i].Headers, remaining[i].Headers);
        }
    }

    [Theory]
    [InlineData("peek")]
    [InlineData("replay")]
    [InlineData("missing-replay")]
    [InlineData("purge")]
    [InlineData("targeted-purge")]
    [InlineData("missing-purge")]
    public async Task CancelledAdministration_ReleasesReceivedDeadLetters(string operation)
    {
        await using var transport = new InMemoryTransport();
        await using var provider = BuildHost(transport, new HandlerSignal());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestCancellationToken);
        var client = new CancelAfterReceiveClient(cancellation);
        var handler = new QueueAdministrationHandler(provider.GetRequiredService<QueueTopology>(),
            provider.GetRequiredService<IQueueWorkerRegistry>(), client, NullLogger<QueueAdministrationHandler>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            switch (operation)
            {
                case "peek": await handler.HandleAsync(new ListDeadLetters("PoisonBodyMessage", 3), cancellation.Token); break;
                case "replay": await handler.HandleAsync(new ReplayDeadLetters("PoisonBodyMessage"), cancellation.Token); break;
                case "missing-replay": await handler.HandleAsync(new ReplayDeadLetters("PoisonBodyMessage", MessageId: "missing"), cancellation.Token); break;
                case "purge": await handler.HandleAsync(new PurgeDeadLetters("PoisonBodyMessage"), cancellation.Token); break;
                case "targeted-purge": await handler.HandleAsync(new PurgeDeadLetters("PoisonBodyMessage", MessageId: "one"), cancellation.Token); break;
                case "missing-purge": await handler.HandleAsync(new PurgeDeadLetters("PoisonBodyMessage", MessageId: "missing"), cancellation.Token); break;
            }
        });
        Assert.Equal(["one", "two"], client.Released.Order());
    }

    private sealed class CancelAfterReceiveClient(CancellationTokenSource cancellation) : IQueueClient
    {
        public List<string> Released { get; } = [];
        public Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken ct = default)
            => throw new IOException("Transport unavailable");

        public Task<IReadOnlyList<QueueMessage>> ReceiveDeadLettersAsync(string queueName, int maxCount, TimeSpan waitTime, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<QueueMessage>>([Create("one"), Create("two")]);
        }

        private static QueueMessage Create(string id) => new()
        {
            Id = id, QueueName = "PoisonBodyMessage-dead-letter", Body = Encoding.UTF8.GetBytes("{}"),
            Headers = new Dictionary<string, string> { [MessageHeaders.OriginalQueueName] = "PoisonBodyMessage" }
        };

        public Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public Task CompleteAsync(QueueMessage message, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Released.Add(message.Id);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken ct = default) => throw new NotSupportedException();
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
