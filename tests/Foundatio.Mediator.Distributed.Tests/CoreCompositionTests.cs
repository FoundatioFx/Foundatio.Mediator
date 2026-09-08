using System.Collections.Concurrent;
using Foundatio.Jobs;
using Foundatio.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Distributed.Tests;

public sealed class CoreCompositionTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private static Task<TestApplication> StartAsync(WorkerSelection? workers = null) => TestApplication.StartAsync(workers: workers,
        configure: services => services.AddSingleton<CompositionLog>().AddScoped<CompositionProbe>());

    [Fact]
    public async Task QueueAttribute_UsesOrdinaryValidationAndShortCircuitLifecycle()
    {
        await using var app = await StartAsync();
        var log = app.Services.GetRequiredService<CompositionLog>();
        var accepted = await app.Mediator.EnqueueAsync(new CompositionWork("  report  "), CT);
        Assert.True(accepted.IsSuccess);
        await app.DrainAsync();

        Assert.Contains("caller:before", log.Events);
        Assert.Contains("caller:finally", log.Events);
        Assert.DoesNotContain("caller:after", log.Events);
        Assert.Contains("worker:before", log.Events);
        Assert.Contains("worker:after", log.Events);
        Assert.Contains("worker:finally", log.Events);
        Assert.Contains("handled:report", log.Events);
        Assert.Equal(JobStatus.Completed, (await app.Store.GetAsync(accepted.Value.JobId!, CT))!.Status);
        var probe = Assert.Single(log.Probes);
        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task ValidationRejectsBeforeSendingOrConstructingTheHandler()
    {
        await using var app = await StartAsync();
        var result = await app.Mediator.EnqueueAsync(new CompositionWork("  "), CT);
        var log = app.Services.GetRequiredService<CompositionLog>();
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Empty(app.Harness.SentMessages);
        Assert.Empty(log.Probes);
        Assert.Contains("caller:finally", log.Events);
        Assert.DoesNotContain("caller:after", log.Events);
    }

    [Fact]
    public async Task ScopedPerInvoke_KeepsNestedInvocationsInItsOwnedScope()
    {
        await using var app = await StartAsync();
        var accepted = await app.Mediator.EnqueueAsync(new CompositionWork("scope"), CT);
        await app.DrainAsync();
        var probe = Assert.Single(app.Services.GetRequiredService<CompositionLog>().Probes);
        Assert.Equal(probe.Id, probe.NestedId);
        Assert.True(probe.Disposed);
        Assert.Equal(JobStatus.Completed, (await app.Store.GetAsync(accepted.Value.JobId!, CT))!.Status);
    }

    [Fact]
    public async Task InvokeAsync_OnQueuedHandlerReturnsAcceptanceWithoutRunningHandler()
    {
        await using var app = await StartAsync(WorkerSelection.None);
        var result = await app.Mediator.InvokeAsync<Result>(new CompositionWork("later"), CT);
        Assert.Equal(ResultStatus.Accepted, result.Status);
        Assert.Single(app.Harness.Sent<CompositionWork>());
        Assert.Empty(app.Services.GetRequiredService<CompositionLog>().Probes);
    }

    [Fact]
    public async Task CascadingTuple_WithExecuteMiddlewarePublishesOnlyAfterProcessing()
    {
        await using var app = await StartAsync(WorkerSelection.None);
        var accepted = await app.Mediator.EnqueueAsync(new CompositionTuple("later"), CT);
        Assert.True(accepted.IsSuccess);
        var log = app.Services.GetRequiredService<CompositionLog>();
        Assert.Contains("caller:execute", log.Events);
        Assert.DoesNotContain(log.Events, value => value.StartsWith("cascade:"));

        // A separate worker consumes the same bus; the original node remains enqueue-only.
        await using var worker = await TestApplication.StartAsync(transport: app.Transport, store: app.Store,
            configure: services => services.AddSingleton(log).AddScoped<CompositionProbe>());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CT);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await app.Store.WaitForCompletionAsync(accepted.Value.JobId!, timeout.Token);
        Assert.Contains("worker:execute", log.Events);
        Assert.Single(log.Events, value => value == "cascade:later");
    }

    [Fact]
    public async Task WorkerFailure_RunsFinallyAndPreservesNormalExceptionHandling()
    {
        await using var app = await StartAsync();
        var accepted = await app.Mediator.EnqueueAsync(new CompositionWork("throw"), CT);
        await app.DrainAsync();
        var log = app.Services.GetRequiredService<CompositionLog>();
        Assert.Contains("worker:failed", log.Events);
        Assert.DoesNotContain("worker:after", log.Events);
        Assert.Equal(JobStatus.Failed, (await app.Store.GetAsync(accepted.Value.JobId!, CT))!.Status);
        Assert.Single(app.Harness.DeadLetteredMessages);
    }
}

public sealed class CompositionLog
{
    public ConcurrentQueue<string> Events { get; } = new();
    public ConcurrentQueue<CompositionProbe> Probes { get; } = new();
}

public sealed class CompositionProbe : IDisposable
{
    public CompositionProbe(CompositionLog log) => log.Probes.Enqueue(this);
    public Guid Id { get; } = Guid.NewGuid();
    public Guid NestedId { get; set; }
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

public record CompositionWork(string Value);
public record CompositionScopeQuery;
public class CompositionScopeHandler
{
    public Guid Handle(CompositionScopeQuery message, CompositionProbe probe) => probe.Id;
}

[Queue(TrackProgress = true, MaxAttempts = 1)]
[Handler(Lifetime = MediatorLifetime.ScopedPerInvoke)]
[UseMiddleware(typeof(CompositionLifecycleMiddleware))]
public class CompositionWorkHandler(CompositionProbe probe, CompositionLog log)
{
    public async Task<Result> HandleAsync(CompositionWork message, IMediator mediator, CancellationToken ct)
    {
        if (message.Value == "throw") throw new InvalidOperationException("Worker failure");
        probe.NestedId = await mediator.InvokeAsync<Guid>(new CompositionScopeQuery(), ct);
        log.Events.Enqueue("handled:" + message.Value);
        return Result.Ok();
    }
}

[Middleware(ExplicitOnly = true, OrderBefore = [typeof(QueueMiddleware)])]
public static class CompositionLifecycleMiddleware
{
    public static HandlerResult Before(CompositionWork message, MessageProcessingContext? context, CompositionLog log)
    {
        log.Events.Enqueue((context is null ? "caller" : "worker") + ":before");
        return string.IsNullOrWhiteSpace(message.Value)
            ? HandlerResult.ShortCircuit(Result.Invalid("Value is required"))
            : HandlerResult.ContinueWith(message with { Value = message.Value.Trim() });
    }

    public static void After(CompositionWork message, MessageProcessingContext? context, CompositionLog log)
        => log.Events.Enqueue((context is null ? "caller" : "worker") + ":after");

    public static void Finally(CompositionWork message, MessageProcessingContext? context, CompositionLog log, Exception? exception)
        => log.Events.Enqueue((context is null ? "caller" : "worker") + (exception is null ? ":finally" : ":failed"));
}

public record CompositionTuple(string Value);
public record CompositionEvent(string Value);
[Queue(TrackProgress = true)]
[UseMiddleware(typeof(CompositionExecuteMiddleware))]
public class CompositionTupleHandler
{
    public (Result<string>, CompositionEvent?) Handle(CompositionTuple message)
        => (Result.Ok(message.Value), new CompositionEvent(message.Value));
}

[Middleware(ExplicitOnly = true)]
public static class CompositionExecuteMiddleware
{
    public static async ValueTask<object?> ExecuteAsync(object message, HandlerExecutionDelegate next,
        MessageProcessingContext? context, CompositionLog log)
    {
        log.Events.Enqueue((context is null ? "caller" : "worker") + ":execute");
        return await next();
    }
}

public class CompositionEventHandler
{
    public void Handle(CompositionEvent message, CompositionLog log) => log.Events.Enqueue("cascade:" + message.Value);
}
