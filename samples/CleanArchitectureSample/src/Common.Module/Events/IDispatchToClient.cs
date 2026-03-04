namespace Common.Module.Events;

/// <summary>
/// Marker interface for events that should be dispatched to connected clients (e.g., via SSE).
/// Handlers in the Api project can listen for this interface to push real-time updates.
/// </summary>
public interface IDispatchToClient
{
}
