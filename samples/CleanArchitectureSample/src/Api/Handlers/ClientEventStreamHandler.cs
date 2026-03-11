using System.Runtime.CompilerServices;
using Common.Module.Events;
using Foundatio.Mediator;

namespace Api.Handlers;

/// <summary>
/// A typed wrapper around a domain event for SSE streaming.
/// Contains the event type name and the event data.
/// </summary>
public record ClientEvent(string EventType, object Data);

/// <summary>
/// Message to subscribe to real-time client events via SSE.
/// </summary>
public record GetEventStream;

/// <summary>
/// Streaming handler that subscribes to all IDispatchToClient notifications via the
/// mediator's built-in subscription support and streams them as SSE events.
/// </summary>
public class ClientEventStreamHandler(IMediator mediator)
{
    [HandlerEndpoint(
        Streaming = EndpointStreaming.ServerSentEvents,
        Summary = "Subscribe to real-time domain events via Server-Sent Events")]
    public async IAsyncEnumerable<ClientEvent> Handle(
        GetEventStream message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(cancellationToken: cancellationToken))
        {
            yield return new ClientEvent(evt.GetType().Name, evt);
        }
    }
}
