using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Distributed.Tests;

public record StagedCommand(string Value);
public record StagedEvent(string Value);
public record StageState;
public record BothState;

public sealed class StageLog
{
    public ConcurrentQueue<string> Entries { get; } = new();
    public ConcurrentBag<StageTenant> Tenants { get; } = new();
    public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class StageTenant(StageLog log) : IDisposable
{
    public string? Name { get; set; }
    public bool Disposed { get; private set; }
    public void Dispose() { Disposed = true; log.Tenants.Add(this); }
}

public sealed class ScopedStageHeaderProvider(StageTenant tenant) : IQueueHeaderProvider
{
    public void Enrich(object message, IDictionary<string, string> headers) => headers["tenant"] = tenant.Name!;
    public void Restore(IReadOnlyDictionary<string, string> headers, CallContext context)
        => tenant.Name = headers.TryGetValue("tenant", out var name) ? name : throw new InvalidOperationException("Required tenant missing");
}

[Middleware(ExplicitOnly = true, Stage = MiddlewareStage.Enqueue)]
public class StageValidationMiddleware
{
    public HandlerResult Before(StagedCommand message, StageLog log)
    {
        log.Entries.Enqueue("validate");
        return message.Value == "invalid" ? HandlerResult.ShortCircuit(Result.Invalid("Invalid command"))
            : HandlerResult.ContinueWith(message with { Value = "enriched:" + message.Value });
    }
}

[Middleware(ExplicitOnly = true, Stage = MiddlewareStage.Enqueue)]
public class StageEnqueueMiddleware
{
    public StageState Before(StagedCommand message, StageLog log) { log.Entries.Enqueue("enqueue.before"); return new(); }
    public void After(StagedCommand message, StageState state, StageLog log) => log.Entries.Enqueue("enqueue.after");
    public void Finally(StagedCommand message, StageState state, Exception? exception, StageLog log) => log.Entries.Enqueue("enqueue.finally");
    public async ValueTask<object?> ExecuteAsync(StagedCommand message, HandlerExecutionDelegate next, StageLog log)
    {
        log.Entries.Enqueue("enqueue.execute");
        try { return await next(); }
        finally { log.Entries.Enqueue("enqueue.exit"); }
    }
}

[Middleware(ExplicitOnly = true)]
public class StageProcessingMiddleware(StageTenant tenant)
{
    public StageState Before(StagedCommand message, StageLog log) { log.Entries.Enqueue("processing.before:" + tenant.Name); return new(); }
    public void After(StagedCommand message, StageState state, StageLog log) => log.Entries.Enqueue("processing.after");
    public void Finally(StagedCommand message, StageState state, Exception? exception, StageLog log) => log.Entries.Enqueue("processing.finally");
    public async ValueTask<object?> ExecuteAsync(StagedCommand message, HandlerExecutionDelegate next, StageLog log)
    {
        log.Entries.Enqueue("processing.execute");
        try { return await next(); }
        finally { log.Entries.Enqueue("processing.exit"); }
    }
}

[Middleware(ExplicitOnly = true, Stage = MiddlewareStage.Both)]
public class StageBothMiddleware
{
    public BothState Before(StagedCommand message, StageLog log) { log.Entries.Enqueue("both.before"); return new(); }
    public void After(StagedCommand message, BothState state, StageLog log) => log.Entries.Enqueue("both.after");
    public void Finally(StagedCommand message, BothState state, StageLog log) => log.Entries.Enqueue("both.finally");
}

[Queue(QueueName = "staged", TrackProgress = true, MaxAttempts = 1)]
[Handler(Lifetime = MediatorLifetime.ScopedPerInvoke)]
[UseMiddleware(typeof(StageEnqueueMiddleware), Order = 0)]
[UseMiddleware(typeof(StageValidationMiddleware), Order = 1)]
[UseMiddleware(typeof(StageBothMiddleware), Order = 2)]
[UseMiddleware(typeof(StageProcessingMiddleware), Order = 3)]
public class StagedCommandHandler(StageTenant tenant)
{
    public (Result<string>, StagedEvent) Handle(StagedCommand message, StageLog log)
    {
        log.Entries.Enqueue($"handle:{tenant.Name}:{message.Value}");
        return (Result.Ok("done"), new(message.Value));
    }
}

public class StagedEventHandler(StageLog log)
{
    public void Handle(StagedEvent message) { log.Entries.Enqueue("event:" + message.Value); log.Done.TrySetResult(); }
}

public class QueueMiddlewareStageTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enqueue_InvalidInput_RunsCleanupWithoutSendingOrCreatingJob(bool runtime)
    {
        var log = new StageLog();
        await using var provider = CreateProvider(log);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StageTenant>().Name = "acme";
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = runtime ? await mediator.InvokeAsync<Result<string>>((object)new StagedCommand("invalid"), CT)
            : await mediator.InvokeAsync<Result<string>>(new StagedCommand("invalid"), CT);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(["enqueue.execute", "enqueue.before", "validate", "enqueue.finally", "enqueue.exit"], log.Entries);
        Assert.Equal(0, provider.GetRequiredService<InMemoryQueueClient>().GetPendingCount("staged"));
        Assert.Empty(await provider.GetRequiredService<IQueueJobStateStore>().GetJobsByStatusAsync("staged", QueueJobStatus.Queued, cancellationToken: CT));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stages_PreserveEnrichmentAndScopes_AndCascadeOnlyOnWorker(bool runtime)
    {
        var log = new StageLog();
        await using var provider = CreateProvider(log);
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<StageTenant>().Name = "acme";
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = runtime ? await mediator.InvokeAsync<Result<string>>((object)new StagedCommand("valid"), CT)
                : await mediator.InvokeAsync<Result<string>>(new StagedCommand("valid"), CT);
            Assert.Equal(ResultStatus.Accepted, result.Status);
            Assert.Equal(["enqueue.execute", "enqueue.before", "validate", "both.before", "both.after", "enqueue.after", "both.finally", "enqueue.finally", "enqueue.exit"], log.Entries);
        }
        Assert.Single(log.Tenants);
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            await log.Done.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
            Assert.Equal(["enqueue.execute", "enqueue.before", "validate", "both.before", "both.after", "enqueue.after", "both.finally", "enqueue.finally", "enqueue.exit",
                "processing.execute", "both.before", "processing.before:acme", "handle:acme:enriched:valid", "processing.after", "both.after", "processing.finally", "both.finally", "processing.exit", "event:enriched:valid"], log.Entries);
        }
        finally { await hosted.StopAllAsync(); }
        Assert.Equal(2, log.Tenants.Count);
        Assert.All(log.Tenants, tenant => Assert.True(tenant.Disposed));
    }

    [Fact]
    public async Task RequiredContextMissing_FailsDeliveryBeforeProcessingMiddlewareAndHandler()
    {
        var log = new StageLog();
        await using var provider = CreateProvider(log);
        var client = provider.GetRequiredService<InMemoryQueueClient>();
        await client.SendAsync("staged", [new QueueEntry
        {
            Body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new StagedCommand("valid")),
            Headers = new Dictionary<string, string> { [MessageHeaders.MessageType] = typeof(StagedCommand).FullName! }
        }], CT);
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            var deadLetters = await ((IQueueClient)client).ReceiveDeadLettersAsync("staged", 1, TimeSpan.FromSeconds(5), CT);
            var deadLetter = Assert.Single(deadLetters);
            Assert.Contains("Required tenant missing", deadLetter.Headers[MessageHeaders.DeadLetterReason]);
            Assert.Empty(log.Entries);
            await client.CompleteAsync(deadLetter, CT);
        }
        finally { await hosted.StopAllAsync(); }
        Assert.Single(log.Tenants);
        Assert.True(log.Tenants.Single().Disposed);
    }

    private static ServiceProvider CreateProvider(StageLog log)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(log);
        services.AddScoped<StageTenant>();
        services.AddSingleton<InMemoryQueueClient>();
        services.AddSingleton<IQueueClient>(sp => sp.GetRequiredService<InMemoryQueueClient>());
        services.AddMediator(builder => builder.AddAssembly<StagedCommandHandler>().SetMediatorLifetime(ServiceLifetime.Scoped))
            .AddDistributedQueues(options => options.Workers = WorkerSelection.Only("staged"))
            .AddQueueHeaderProvider<ScopedStageHeaderProvider>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
