using Common.Module.Events;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;
using Api.Services;

namespace Api.Handlers;

/// <summary>
/// Handler that listens for all events implementing IDispatchToClient.
/// This handler is in the Api project but can receive events published from any module
/// (e.g., OrderCreated from Orders.Module) thanks to runtime DI handler discovery.
/// Pushes events to connected SSE clients via the ClientEventBroadcaster.
/// </summary>
[Handler]
public class ClientDispatchHandler(
    ClientEventBroadcaster broadcaster,
    ILogger<ClientDispatchHandler> logger)
{
    public Task HandleAsync(IDispatchToClient message, CancellationToken cancellationToken)
    {
        var eventType = message.GetType().Name;

        logger.LogInformation(
            "Dispatching {EventType} to connected clients via SSE",
            eventType);

        // Broadcast the event to all connected SSE subscribers
        broadcaster.Broadcast(new ClientEvent(eventType, message));

        return Task.CompletedTask;
    }
}
