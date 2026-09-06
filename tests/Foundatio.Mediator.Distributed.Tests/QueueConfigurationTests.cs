using System.Text.Json;
using Foundatio.Mediator.Distributed.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed.Tests;

public class QueueConfigurationTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public async Task SharedStateGuard_ValidatesEffectiveDecoratedClient(bool transportFirst, bool inMemory, bool allowLocal)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var client = new RecordingQueueClient(inMemory ? new InMemoryQueueClient() : new UncertainQueueClient());
        if (transportFirst) services.AddSingleton<IQueueClient>(client);
        services.AddMediator(options => options.AddAssembly<MetadataTrackedCommandHandler>()).AddDistributedQueues(options =>
        {
            options.Workers = WorkerSelection.None;
            options.AllowProcessLocalJobStateForDevelopment = allowLocal;
        });
        if (!transportFirst) services.AddSingleton<IQueueClient>(client);
        await using var provider = services.BuildServiceProvider();
        var validation = provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>().Single();
        if (inMemory || allowLocal)
            await validation.StartingAsync(CT);
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validation.StartingAsync(CT));
            Assert.Contains("UseRedisJobState", error.Message);
        }
    }

    [Fact]
    public async Task SharedDefaults_ApplyToQueuesAndNotifications_WhileSelectorsUseLogicalNames()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serializer = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        services.AddMediator(options => options.AddAssembly<MetadataTrackedCommandHandler>())
            .ConfigureDistributed(options => { options.ResourcePrefix = "test-env"; options.JsonSerializerOptions = serializer; })
            .AddDistributedQueues(options =>
            {
                options.Workers = WorkerSelection.Only("MetadataTrackedCommand");
                options.AllowInMemoryWithoutWorkers = true;
                options.QueueOverrides["MetadataTrackedCommand"] = settings => { settings.Concurrency = 4; settings.MaxAttempts = 7; };
            })
            .AddDistributedNotifications();
        await using var provider = services.BuildServiceProvider();
        var queue = provider.GetRequiredService<QueueTopology>().GetByQueueName("test-env-MetadataTrackedCommand")!;
        Assert.True(queue.WorkerRunsHere);
        Assert.Equal(4, queue.Settings.Concurrency);
        Assert.Equal(7, queue.Settings.MaxAttempts);
        Assert.Same(serializer, provider.GetRequiredService<DistributedQueueOptions>().JsonSerializerOptions);
        Assert.Same(serializer, provider.GetRequiredService<DistributedNotificationOptions>().JsonSerializerOptions);
        Assert.Equal("test-env", provider.GetRequiredService<DistributedNotificationOptions>().ResourcePrefix);
    }

    [Theory]
    [InlineData("none,exports")]
    [InlineData("exports,")]
    [InlineData("!")]
    [InlineData("all,exports")]
    [InlineData("exports,!exports")]
    [InlineData("exports;imports")]
    [InlineData("-imports")]
    [InlineData("* exports")]
    public void WorkerSelection_RejectsMalformedExpressions(string selection)
        => Assert.Throws<ArgumentException>(() => WorkerSelection.Parse(selection));

    [Fact]
    public void WorkerSelection_RejectsUnmatchedNamesWithAvailableSubscriptions()
    {
        var services = new ServiceCollection();
        var builder = services.AddMediator(options => options.AddAssembly<MetadataTrackedCommandHandler>());
        var error = Assert.Throws<InvalidOperationException>(() => builder.AddDistributedQueues(options => options.Workers = WorkerSelection.Only("typo")));
        Assert.Contains("MetadataTrackedCommand", error.Message);
        Assert.Contains("typo", error.Message);
    }

    [Fact]
    public async Task Enqueue_TransportFailsAfterAccepting_PreservesReceiptAndUnknownState()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var client = new UncertainQueueClient();
        services.AddSingleton<IQueueClient>(client);
        services.AddMediator(options => options.AddAssembly<MetadataTrackedCommandHandler>()).AddDistributedQueues(options => options.Workers = WorkerSelection.None);
        await using var provider = services.BuildServiceProvider();
        var exception = await Assert.ThrowsAsync<QueueEnqueueException>(async () =>
            await provider.GetRequiredService<IMediator>().EnqueueAsync(new MetadataTrackedCommand("data", "tenant"), CT));
        Assert.Equal("MetadataTrackedCommand", exception.Receipt.QueueName);
        Assert.NotNull(exception.Receipt.JobId);
        var job = await provider.GetRequiredService<IQueueJobStateStore>().GetJobStateAsync(exception.Receipt.JobId!, CT);
        Assert.Equal(QueueJobStatus.EnqueueUnknown, job!.Status);
        Assert.Equal(1, client.Inner.GetPendingCount(exception.Receipt.QueueName));
    }

    private sealed class UncertainQueueClient : IQueueClient
    {
        public InMemoryQueueClient Inner { get; } = new();
        public async Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
        {
            await Inner.SendAsync(queueName, entries, cancellationToken);
            throw new IOException("Connection lost after acceptance");
        }
        public Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken cancellationToken = default) => Inner.ReceiveAsync(queueName, maxCount, visibilityTimeout, cancellationToken);
        public Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default) => Inner.CompleteAsync(message, cancellationToken);
        public Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default) => Inner.AbandonAsync(message, delay, cancellationToken);
        public Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default) => Inner.RenewTimeoutAsync(message, extension, cancellationToken);
        public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default) => Inner.DeadLetterAsync(message, reason, cancellationToken);
        public Task EnsureQueuesAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken cancellationToken = default) => ((IQueueClient)Inner).EnsureQueuesAsync(queues, cancellationToken);
        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
