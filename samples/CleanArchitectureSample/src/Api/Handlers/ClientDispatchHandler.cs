using Common.Module.Events;
using Foundatio.Mediator;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Api.Hubs;

namespace Api.Handlers;

/// <summary>
/// Handler that listens for all events implementing IDispatchToClient.
/// This handler is in the Api project but can receive events published from any module
/// (e.g., OrderCreated from Orders.Module) thanks to runtime DI handler discovery.
/// Pushes events to connected clients via SignalR.
/// </summary>
[Handler]
public class ClientDispatchHandler(
    IHubContext<EventHub> hubContext,
    ILogger<ClientDispatchHandler> logger)
{
    public async Task HandleAsync(IDispatchToClient message, CancellationToken cancellationToken)
    {
        var eventType = message.GetType().Name;

        logger.LogInformation(
            "Dispatching {EventType} to connected clients via SignalR",
            eventType);

        // Send the event to all connected clients
        await hubContext.Clients.All.SendAsync(
            eventType,
            message,
            cancellationToken);
    }
}
