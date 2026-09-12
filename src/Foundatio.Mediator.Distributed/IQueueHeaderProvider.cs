namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Adds application headers to queued messages on enqueue and restores them into the handler's
/// <see cref="CallContext"/> on the worker, for example a tenant scope or request id.
/// Register implementations in DI; every registered provider runs in order.
/// </summary>
public interface IQueueHeaderProvider
{
    /// <summary>
    /// Adds headers for <paramref name="message"/> before it is sent.
    /// </summary>
    void Enrich(object message, IDictionary<string, string> headers);

    /// <summary>
    /// Restores context from <paramref name="headers"/> before the handler runs. Values placed in
    /// <paramref name="callContext"/> are available as handler and middleware parameters.
    /// </summary>
    void Restore(IReadOnlyDictionary<string, string> headers, CallContext callContext);
}
