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
/// Subscribe to real-time domain events via Server-Sent Events.
/// </summary>
public class EventHandler(IMediator mediator)
{
    [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
    public async IAsyncEnumerable<ClientEvent> Handle(
        GetEventStream message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in mediator.SubscribeAsync<IDispatchToClient>(cancellationToken))
        {
            yield return new ClientEvent(evt.GetType().Name, evt);
        }
    }
}
