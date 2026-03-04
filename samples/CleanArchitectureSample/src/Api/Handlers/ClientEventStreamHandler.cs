using Api.Services;
using Foundatio.Mediator;

namespace Api.Handlers;

/// <summary>
/// Message to subscribe to real-time client events via SSE.
/// </summary>
public record SubscribeToClientEvents;

/// <summary>
/// Streaming handler that returns an IAsyncEnumerable of ClientEvent.
/// The source generator auto-generates an SSE endpoint for this handler.
/// Replaces the SignalR EventHub for real-time event streaming.
/// </summary>
[Handler]
public class ClientEventStreamHandler(ClientEventBroadcaster broadcaster)
{
    [HandlerEndpoint(
        Route = "/events/stream",
        Streaming = EndpointStreaming.ServerSentEvents,
        SseEventType = "event",
        Summary = "Subscribe to real-time domain events via Server-Sent Events")]
    [HandlerAllowAnonymous]
    public IAsyncEnumerable<ClientEvent> Handle(
        SubscribeToClientEvents message,
        CancellationToken cancellationToken)
    {
        return broadcaster.SubscribeAsync(cancellationToken);
    }
}
