namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Optional transport/decorator hook for test drains. ReceiveAsync must register delivered receipts
/// before returning them; the worker reports completion after processing, settlement, and state updates.
/// </summary>
public interface IQueueProcessingObserver
{
    /// <summary>Finishes observation of this delivery, including shutdown abandonment. Implementations must not throw.</summary>
    void ProcessingFinished(QueueMessage message);
}
