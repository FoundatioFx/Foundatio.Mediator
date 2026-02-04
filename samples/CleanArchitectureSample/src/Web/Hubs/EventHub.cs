using Microsoft.AspNetCore.SignalR;

namespace Web.Hubs;

/// <summary>
/// SignalR hub for pushing real-time events to connected clients.
/// </summary>
public class EventHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
