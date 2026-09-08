using Foundatio.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>Declares the discovered destinations through Foundatio's configured topology policy.</summary>
internal sealed class DistributedInfrastructureInitializer(
    IMessageTransport transport,
    MessagingTopologyOptions? policy,
    DistributedInfrastructureOptions options,
    DistributedInfrastructureReady ready,
    ILogger<DistributedInfrastructureInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ready.MarkStarted();
        try
        {
            var declarations = options.QueueNames.Concat(options.TopicNames)
                .Select(address => new DestinationDeclaration { Address = address }).ToList();
            if (transport is not ISupportsDeadLetterSink)
                declarations.AddRange(options.QueueNames.Select(source => new DestinationDeclaration { Address = DestinationAddress.ForQueue(source.Name + ".deadletter") }));
            var mode = policy?.Mode ?? TopologyMode.Ensure;
            if (mode != TopologyMode.None)
            {
                var provisioning = transport as ISupportsProvisioning
                    ?? throw new InvalidOperationException("The configured transport cannot provision or validate destinations. Configure TopologyMode.None when provisioning externally.");
                if (mode == TopologyMode.Ensure)
                    await provisioning.EnsureAsync(declarations, cancellationToken).ConfigureAwait(false);
                else
                    foreach (var declaration in declarations)
                        if (!await provisioning.ExistsAsync(declaration.Address, cancellationToken).ConfigureAwait(false))
                            throw new InvalidOperationException($"Destination '{declaration.Address}' has not been provisioned.");
            }
            ready.SetReady();
            logger.LogInformation("Foundatio messaging is ready with {Count} declared destinations", declarations.Count);
        }
        catch (Exception exception) { ready.SetFailed(exception); throw; }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Readiness of this application's discovered messaging destinations.</summary>
public sealed class DistributedInfrastructureReady
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsStarted { get; private set; }
    public bool IsReady => _ready.Task.IsCompletedSuccessfully;
    public Task WaitAsync(CancellationToken cancellationToken = default) => _ready.Task.WaitAsync(cancellationToken);
    internal void MarkStarted() => IsStarted = true;
    internal void SetReady() => _ready.TrySetResult();
    internal void SetFailed(Exception exception) => _ready.TrySetException(exception);
}

internal sealed class DistributedInfrastructureOptions
{
    public List<DestinationAddress> QueueNames { get; } = [];
    public List<DestinationAddress> TopicNames { get; } = [];
}
