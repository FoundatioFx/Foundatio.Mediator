using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Distributed.Tests;

public interface IRoutingCommand;
public record RoutingCommand : IRoutingCommand;

[Queue(QueueName = "routing-shared")]
public class AInterfaceRoutingHandler
{
    public Result Handle(IRoutingCommand message) => Result.Ok();
}

[Queue(QueueName = "routing-shared")]
[Handler(OrderBefore = [typeof(AInterfaceRoutingHandler)])]
public class ZExactRoutingHandler
{
    public Result Handle(RoutingCommand message) => Result.Ok();
}

public record RoutingEvent;

[Queue(MaxAttempts = 1)]
public class DefaultAuditHandler(HandlerSignal signal)
{
    public void Handle(RoutingEvent message) => signal.Record("audit");
}

[Queue(MaxAttempts = 1)]
public class DefaultWebhookHandler
{
    public void Handle(RoutingEvent message) => throw new InvalidOperationException("webhook offline");
}

public class QueueRoutingReliabilityTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invoke_ExactHandlerWithEarlierInterfaceHandler_AlwaysEnqueues()
    {
        await using var client = new InMemoryQueueClient();
        await using var provider = CreateProvider(client);
        var mediator = provider.GetRequiredService<IMediator>();
        var result = await mediator.InvokeAsync<Result>(new RoutingCommand(), CT);
        Assert.Equal(ResultStatus.Accepted, result.Status);
        Assert.Equal(1, client.GetPendingCount("routing-shared"));
        // Force runtime dispatch too; a typed interceptor and the runtime must select the same destination.
        object runtimeCommand = new RoutingCommand();
        await mediator.InvokeAsync<Result>(runtimeCommand, CT);
        Assert.Equal(2, client.GetPendingCount("routing-shared"));
    }

    [Fact]
    public async Task Publish_ExplicitSharedGroup_SendsOnceForActualMatchingHandlers()
    {
        await using var client = new InMemoryQueueClient();
        await using var provider = CreateProvider(client);
        Assert.Equal([nameof(ZExactRoutingHandler), nameof(AInterfaceRoutingHandler)],
            provider.GetRequiredService<QueueTopology>().GetByQueueName("routing-shared")!.Handlers.Select(handler => handler.SourceHandlerName));
        await provider.GetRequiredService<IMediator>().PublishAsync(new RoutingCommand(), CT);
        Assert.Equal(1, client.GetPendingCount("routing-shared"));
    }

    [Fact]
    public async Task DefaultSubscriptions_IsolateSuccessfulHandlerFromFailedHandler()
    {
        await using var client = new InMemoryQueueClient();
        var signal = new HandlerSignal();
        await using var provider = CreateProvider(client, signal);
        var topology = provider.GetRequiredService<QueueTopology>();
        Assert.Single(topology.GetByQueueName("DefaultAudit-RoutingEvent")!.Handlers);
        Assert.Single(topology.GetByQueueName("DefaultWebhook-RoutingEvent")!.Handlers);
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            await provider.GetRequiredService<IMediator>().PublishAsync(new RoutingEvent(), CT);
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(5));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CT);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (client.GetDeadLetterCount("DefaultWebhook-RoutingEvent") != 1)
                await Task.Delay(10, timeout.Token);
            Assert.Equal(["audit"], signal.Values);
            Assert.Equal(0, client.GetDeadLetterCount("DefaultAudit-RoutingEvent"));
        }
        finally { await hosted.StopAllAsync(); }
    }

    [Fact]
    public void AddDistributedQueues_UnsupportedResponse_FailsBeforeFirstSend()
    {
        var services = new ServiceCollection();
        var builder = services.AddMediator(options => options.AddAssembly<IMediator>());
        var registry = new HandlerRegistry();
        registry.AddHandler(new HandlerRegistration(
            typeof(QueuedQuery).FullName!, "invalid-query", (_, _, _, _, _, _) => new((object?)null), null, true,
            sourceHandlerName: nameof(QueuedQueryHandler), methodName: "Handle",
            sourceHandlerTypeName: typeof(QueuedQueryHandler).FullName!,
            sourceMethodParameterTypeNames: [typeof(QueuedQuery).FullName!],
            attributeMetadata: [new(typeof(QueueAttribute).FullName!, HandlerAttributeTarget.HandlerType)]));
        var descriptor = services.Single(service => service.ServiceType == typeof(HandlerRegistry));
        services.Remove(descriptor);
        registry.Freeze();
        services.AddSingleton(registry);
        var error = Assert.Throws<InvalidOperationException>(() => builder.AddDistributedQueues());
        Assert.Contains("System.String", error.Message);
        Assert.Contains("acceptance", error.Message);
    }

    private static ServiceProvider CreateProvider(IQueueClient client, HandlerSignal? signal = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(client);
        services.AddSingleton(signal ?? new HandlerSignal());
        services.AddMediator(builder => builder.AddAssembly<DefaultAuditHandler>())
            .AddDistributedQueues(options => options.Workers = WorkerSelection.Only("DefaultAudit-RoutingEvent", "DefaultWebhook-RoutingEvent"));
        return services.BuildServiceProvider();
    }
}
