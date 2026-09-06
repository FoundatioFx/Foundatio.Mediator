namespace Foundatio.Mediator.Distributed;

/// <summary>
/// The delivery receipt no longer owns the message. Processing must stop; a newer delivery
/// may already be executing. Cancellation cannot undo side effects already performed.
/// </summary>
public sealed class QueueLeaseLostException : Exception
{
    /// <summary>Creates an exception for an expired or invalid delivery receipt.</summary>
    public QueueLeaseLostException(string message, Exception? innerException = null) : base(message, innerException) { }
}
