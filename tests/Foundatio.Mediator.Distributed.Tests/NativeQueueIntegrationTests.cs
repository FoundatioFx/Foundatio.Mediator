using Foundatio.Jobs;
using System.Collections.Concurrent;
using Foundatio;
using Foundatio.Messaging;
using Foundatio.Messaging.Testing;
using Foundatio.Serializer;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed.Tests;

public sealed class NativeQueueIntegrationTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NativeSerializerConfiguration_IsUsedByQueuesAndRemoteNotifications()
    {
        var serializer = new SystemTextJsonSerializer(new JsonSerializerOptions
        {
            Converters = { new EncodedWorkConverter(), new EncodedBroadcastConverter() }
        });
        await using var transport = new InMemoryMessageTransport();
        await using var first = await TestApplication.StartAsync(transport: transport, notifications: true, serializer: serializer);
        await using var second = await TestApplication.StartAsync(transport: transport, notifications: true, serializer: serializer, store: first.Store);
        var accepted = await first.Mediator.EnqueueAsync(new NativeWork("encoded"), CT);
        Assert.True(accepted.IsSuccess);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(CT);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        Assert.Equal(JobStatus.Completed, (await first.Store.WaitForCompletionAsync(accepted.Value.JobId!, deadline.Token)).Status);
        Assert.Contains(first.Log.Events.Concat(second.Log.Events), value => value == "tenant:default");
        await first.Mediator.PublishAsync(new BroadcastEvent("encoded"), CT);
        await second.Log.Broadcast.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
        Assert.Contains("broadcast:encoded", second.Log.Events);
    }

    [Fact]
    public async Task Enqueue_UsesNativeBusContextAndExecutionStore()
    {
        await using var app = await TestApplication.StartAsync();
        var result = await app.Mediator.EnqueueAsync(new NativeWork("hello"), CT);
        Assert.True(result.IsSuccess);
        Assert.Equal("native-work", result.Value.QueueName);
        await app.DrainAsync();
        var state = await app.Store.GetAsync(result.Value.JobId!, CT);
        Assert.Equal(JobStatus.Completed, state!.Status);
        Assert.Equal(100, state.Progress);
        Assert.Equal("hello", state.ProgressMessage);
        Assert.Single(app.Harness.Sent<NativeWork>());
        Assert.Single(app.Harness.Handled<NativeWork>());
        Assert.Single(app.Log.Scopes);
        Assert.All(app.Log.Scopes, scope => Assert.True(scope.Disposed));
    }

    [Fact]
    public async Task EnqueueValidation_ReturnsFailureBeforePersistingOrSending()
    {
        await using var app = await TestApplication.StartAsync();
        var result = await app.Mediator.EnqueueAsync(new NativeWork(""), CT);
        Assert.False(result.IsSuccess);
        Assert.Empty(app.Harness.SentMessages);
        Assert.Equal(0, await app.Store.CountAsync(new JobQuery { QueueName = "native-work", Status = JobStatus.Queued }, CT));
        Assert.Empty(app.Log.Scopes);
    }

    [Fact]
    public async Task RetryableResult_RunsAgainAndKeepsExecutionIdentity()
    {
        await using var app = await TestApplication.StartAsync();
        var accepted = await app.Mediator.EnqueueAsync(new NativeWork("retry"), CT);
        await app.DrainAsync();
        var state = await app.Store.GetAsync(accepted.Value.JobId!, CT);
        Assert.Equal(JobStatus.Completed, state!.Status);
        Assert.Equal(2, state.Attempt);
        Assert.Single(app.Harness.Abandoned<NativeWork>());
        Assert.Equal(2, app.Log.Scopes.Count);
        Assert.Equal(2, app.Log.Scopes.Select(scope => scope.Id).Distinct().Count());
    }

    [Fact]
    public async Task TerminalResult_DeadLettersImmediatelyWithReasonAndFailedState()
    {
        await using var app = await TestApplication.StartAsync();
        var accepted = await app.Mediator.EnqueueAsync(new NativeWork("invalid"), CT);
        await app.DrainAsync();
        var entry = Assert.Single(app.Harness.DeadLetteredMessages);
        Assert.Contains("Invalid", entry.Reason);
        var state = await app.Store.GetAsync(accepted.Value.JobId!, CT);
        Assert.Equal(JobStatus.Failed, state!.Status);
        Assert.Equal(1, state.Attempt);
    }

    [Fact]
    public async Task Publish_DefaultSubscriptionsEachReceiveOneCopy()
    {
        await using var app = await TestApplication.StartAsync();
        await app.Mediator.PublishAsync(new FanoutEvent("order-1"), CT);
        await app.DrainAsync();
        Assert.Equal(2, app.Harness.Sent<FanoutEvent>().Count);
        Assert.Contains("audit:order-1", app.Log.Events);
        Assert.Contains("email:order-1", app.Log.Events);
    }

    [Fact]
    public async Task Publish_SharedQueueSendsOnceAndInvokesBothHandlers()
    {
        await using var app = await TestApplication.StartAsync();
        await app.Mediator.PublishAsync(new SharedEvent("order-2"), CT);
        await app.DrainAsync();
        Assert.Single(app.Harness.Sent<SharedEvent>());
        Assert.Contains("reserve:order-2", app.Log.Events);
        Assert.Contains("confirm:order-2", app.Log.Events);
    }

    [Fact]
    public async Task Processing_RestoresHeadersIntoItsOwnScope()
    {
        await using var app = await TestApplication.StartAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<NativeTenant>().Name = "acme";
            await scope.ServiceProvider.GetRequiredService<IMediator>().EnqueueAsync(new NativeWork("tenant"), CT);
        }
        await app.DrainAsync();
        Assert.Contains("tenant:acme", app.Log.Events);
        Assert.All(app.Log.Scopes, scope => Assert.True(scope.Disposed));
    }

    [Fact]
    public async Task ManualSettlement_RemainsAuthoritativeAfterHandlerException()
    {
        await using var app = await TestApplication.StartAsync();
        await app.Mediator.EnqueueAsync(new ManualWork(), CT);
        await app.DrainAsync();
        Assert.Single(app.Harness.Handled<ManualWork>());
        Assert.Empty(app.Harness.Abandoned<ManualWork>());
        Assert.Empty(app.Harness.DeadLetteredMessages);
    }

    [Fact]
    public async Task EnqueueOnlyHost_ReturnsAcceptanceAndLeavesWorkForOtherWorkers()
    {
        await using var app = await TestApplication.StartAsync(WorkerSelection.None);
        var result = await app.Mediator.EnqueueAsync(new NativeWork("later"), CT);
        Assert.True(result.IsSuccess);
        Assert.Empty(app.Harness.HandledMessages);
        Assert.Equal(JobStatus.Queued, (await app.Store.GetAsync(result.Value.JobId!, CT))!.Status);
        var stats = await app.Administration.GetStatsAsync(DestinationAddress.ForQueue("native-work"), CT);
        Assert.Equal(1, stats.Queued);
    }

    [Fact]
    public async Task QueuedCancellation_IsObservedBeforeInvokingHandler()
    {
        await using var app = await TestApplication.StartAsync(WorkerSelection.None);
        var result = await app.Mediator.EnqueueAsync(new NativeWork("cancelled"), CT);
        Assert.True(await app.Store.RequestCancellationAsync(result.Value.JobId!, CT));
        // A second consumer host shares the native transport and store without a runnable job-store worker.
        await using var worker = await TestApplication.StartAsync(transport: app.Transport, store: app.Store);
        await app.DrainAsync();
        Assert.Equal(JobStatus.Cancelled, (await app.Store.GetAsync(result.Value.JobId!, CT))!.Status);
        Assert.Empty(worker.Log.Scopes);
    }

    [Fact]
    public async Task DeadLetterReplay_CreatesFreshExecutionAndPreservesFailedHistory()
    {
        await using var app = await TestApplication.StartAsync();
        var original = await app.Mediator.EnqueueAsync(new NativeWork("invalid"), CT);
        await app.DrainAsync();
        var dead = Assert.Single(await app.Administration.PeekDeadLettersAsync(DestinationAddress.ForQueue("native-work"), cancellationToken: CT));
        var replay = await app.Mediator.InvokeAsync<Result<DeadLetterReplayResult>>(new ReplayDeadLetters("native-work", MessageId: dead.Id), CT);
        Assert.True(replay.IsSuccess);
        var receipt = Assert.Single(replay.Value.Receipts);
        Assert.NotEqual(original.Value.JobId, receipt.JobId);
        await app.DrainAsync();
        Assert.Equal(JobStatus.Failed, (await app.Store.GetAsync(original.Value.JobId!, CT))!.Status);
        Assert.Equal(JobStatus.Failed, (await app.Store.GetAsync(receipt.JobId!, CT))!.Status);
    }

    [Fact]
    public async Task UnknownWireType_GoesToDeadLetterWithoutLoadingAssemblies()
    {
        await using var app = await TestApplication.StartAsync();
        var result = await app.Transport.SendAsync(DestinationAddress.ForQueue("native-work"), [new TransportMessage
        {
            Body = "{}"u8.ToArray(), ContentType = "application/json",
            Headers = Foundatio.Messaging.MessageHeaders.Create(new Dictionary<string, string> { [KnownHeaders.MessageType] = "unknown.type" })
        }], new TransportSendOptions(), CT);
        result.EnsureAccepted(1);
        await app.DrainAsync();
        Assert.Single(app.Harness.DeadLetteredMessages);
        Assert.Empty(app.Log.Scopes);
    }

    [Fact]
    public async Task NodeNotifications_PublishWithoutLocalHandlersReachesRemoteSubscribers()
    {
        await using var transport = new InMemoryMessageTransport();
        await using var first = await TestApplication.StartAsync(transport: transport, notifications: true);
        await using var second = await TestApplication.StartAsync(transport: transport, notifications: true);
        await using var received = second.Mediator.SubscribeAsync<UnhandledBroadcast>(CT).GetAsyncEnumerator(CT);
        var pending = received.MoveNextAsync().AsTask();
        await first.Mediator.PublishAsync(new UnhandledBroadcast("refresh"), CT);
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5), CT));
        Assert.Equal("refresh", received.Current.Value);
    }

    [Fact]
    public async Task NodeNotifications_RuntimePublishDoesNotRepeatQueuedSideEffects()
    {
        await using var transport = new InMemoryMessageTransport();
        await using var first = await TestApplication.StartAsync(transport: transport, notifications: true);
        await using var second = await TestApplication.StartAsync(transport: transport, notifications: true);
        object message = new BroadcastEvent("runtime");
        await first.Mediator.PublishAsync(message, CT);
        await second.Log.Broadcast.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CT);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!first.Log.Events.Concat(second.Log.Events).Contains("queued-broadcast:runtime"))
            await Task.Delay(10, timeout.Token);
        await Task.Delay(100, CT);
        Assert.Single(first.Log.Events.Concat(second.Log.Events), value => value == "queued-broadcast:runtime");
        Assert.Single(first.Log.Events, value => value == "broadcast:runtime");
        Assert.Single(second.Log.Events, value => value == "broadcast:runtime");
    }

    [Fact]
    public async Task NodeNotifications_ReachOtherNodesOnceWithoutRebroadcast()
    {
        await using var transport = new InMemoryMessageTransport();
        await using var first = await TestApplication.StartAsync(transport: transport, notifications: true);
        await using var second = await TestApplication.StartAsync(transport: transport, notifications: true);
        await first.Mediator.PublishAsync(new BroadcastEvent("changed"), CT);
        await second.Log.Broadcast.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
        await Task.Delay(100, CT);
        Assert.Single(first.Log.Events, value => value == "broadcast:changed");
        Assert.Single(second.Log.Events, value => value == "broadcast:changed");
    }
}

internal sealed class TestApplication(IHost host) : IAsyncDisposable
{
    public IServiceProvider Services => host.Services;
    public IMediator Mediator => Services.GetRequiredService<IMediator>();
    public IMessageTransport Transport => Services.GetRequiredService<IMessageTransport>();
    public IJobRuntimeStore Store => Services.GetRequiredService<IJobRuntimeStore>();
    public MessagingTestHarness Harness => Services.GetRequiredService<MessagingTestHarness>();
    public NativeLog Log => Services.GetRequiredService<NativeLog>();
    public MessageAdministration Administration => new(Transport);
    public Task DrainAsync() => Harness.WaitForIdleAsync(cancellationToken: TestContext.Current.CancellationToken);

    public static async Task<TestApplication> StartAsync(WorkerSelection? workers = null, IMessageTransport? transport = null, IJobRuntimeStore? store = null, bool notifications = false, ITextSerializer? serializer = null, Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        var foundatio = builder.Services.AddFoundatio();
        if (serializer is not null) foundatio.AddSerializer(serializer);
        var messaging = foundatio.Messaging.UseTestHarness();
        foundatio.Jobs.UseInMemory();
        if (transport is not null) messaging.UseTransport(transport);
        if (store is not null) foundatio.Jobs.UseRuntimeStore(store);
        builder.Services.AddSingleton<NativeLog>();
        builder.Services.AddScoped<NativeTenant>();
        builder.Services.AddScoped<NativeScope>();
        configure?.Invoke(builder.Services);
        var mediator = builder.Services.AddMediator(options => options.AddAssembly<NativeWorkHandler>().SetMediatorLifetime(ServiceLifetime.Scoped))
            .AddDistributedQueues(options => { options.Workers = workers ?? WorkerSelection.All; options.ShutdownTimeout = TimeSpan.FromMilliseconds(100); })
            .AddQueueHeaderProvider<NativeTenantHeaders>();
        if (notifications) mediator.AddDistributedNotifications(options => options.Include<BroadcastEvent>().Include<UnhandledBroadcast>());
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return new TestApplication(host);
    }

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync(TestContext.Current.CancellationToken);
        if (host is IAsyncDisposable disposable) await disposable.DisposeAsync(); else host.Dispose();
    }
}

public sealed class NativeLog
{
    public ConcurrentQueue<string> Events { get; } = new();
    public ConcurrentQueue<NativeScope> Scopes { get; } = new();
    public TaskCompletionSource Broadcast { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
public sealed class NativeTenant { public string Name { get; set; } = "default"; }
public sealed class NativeScope(NativeLog log) : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public bool Disposed { get; private set; }
    public void Dispose() { Disposed = true; log.Scopes.Enqueue(this); }
}
public sealed class NativeTenantHeaders(NativeTenant tenant) : IQueueHeaderProvider
{
    public void Enrich(object message, IDictionary<string, string> headers) => headers["tenant"] = tenant.Name;
    public void Restore(IReadOnlyDictionary<string, string> headers, CallContext context) => tenant.Name = headers.GetValueOrDefault("tenant") ?? "default";
}
public record NativeWork(string Value);
[Middleware(ExplicitOnly = true, OrderBefore = [typeof(QueueMiddleware)])]
public class NativeValidationMiddleware
{
    public HandlerResult Before(NativeWork message) => message.Value.Length == 0 ? HandlerResult.ShortCircuit(Result.Invalid("Value is required")) : HandlerResult.Continue();
}
[Queue(QueueName = "native-work", TrackProgress = true, RetryDelaySeconds = 0)]
[UseMiddleware(typeof(NativeValidationMiddleware))]
public class NativeWorkHandler(NativeScope scope, NativeTenant tenant, NativeLog log)
{
    public async Task<Result> HandleAsync(NativeWork message, MessageProcessingContext context, CancellationToken ct)
    {
        _ = scope.Id;
        if (message.Value == "retry" && context.DequeueCount == 1) return Result.Unavailable("Try again");
        if (message.Value == "invalid") return Result.Invalid("Invalid work");
        log.Events.Enqueue("tenant:" + tenant.Name);
        await context.ReportProgressAsync(100, message.Value, ct);
        return Result.Ok();
    }
}
public record FanoutEvent(string Id);
[Queue] public class NativeAuditHandler(NativeLog log) { public void Handle(FanoutEvent message) => log.Events.Enqueue("audit:" + message.Id); }
[Queue] public class NativeEmailHandler(NativeLog log) { public void Handle(FanoutEvent message) => log.Events.Enqueue("email:" + message.Id); }
public record SharedEvent(string Id);
[Queue(QueueName = "shared")] public class NativeReserveHandler(NativeLog log) { public void Handle(SharedEvent message) => log.Events.Enqueue("reserve:" + message.Id); }
[Queue(QueueName = "shared")] public class NativeConfirmHandler(NativeLog log) { public void Handle(SharedEvent message) => log.Events.Enqueue("confirm:" + message.Id); }
public record ManualWork;
[Queue]
public class NativeManualHandler
{
    public async Task HandleAsync(ManualWork message, MessageProcessingContext context, CancellationToken ct)
    {
        await context.CompleteAsync(ct);
        throw new InvalidOperationException("Already committed");
    }
}
public sealed record UnhandledBroadcast(string Value);
public record BroadcastEvent(string Value);
[Queue]
public class NativeBroadcastQueuedHandler(NativeLog log)
{
    public void Handle(BroadcastEvent message) => log.Events.Enqueue("queued-broadcast:" + message.Value);
}
public class NativeBroadcastHandler(NativeLog log)
{
    public void Handle(BroadcastEvent message) { log.Events.Enqueue("broadcast:" + message.Value); log.Broadcast.TrySetResult(); }
}

public sealed class EncodedWorkConverter : JsonConverter<NativeWork>
{
    public override NativeWork Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString()!["encoded:".Length..]);
    public override void Write(Utf8JsonWriter writer, NativeWork value, JsonSerializerOptions options)
        => writer.WriteStringValue("encoded:" + value.Value);
}

public sealed class EncodedBroadcastConverter : JsonConverter<BroadcastEvent>
{
    public override BroadcastEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString()!["encoded:".Length..]);
    public override void Write(Utf8JsonWriter writer, BroadcastEvent value, JsonSerializerOptions options)
        => writer.WriteStringValue("encoded:" + value.Value);
}
