using Foundatio.Mediator;

namespace Common.Module.Events;

/// <summary>
/// Marker for notifications the API streams to connected browsers over SSE. Every implementation crosses
/// the notification bus: the browser's connection may sit on a different node than the one that published.
/// </summary>
public interface IDispatchToClient : INotification
{
}
