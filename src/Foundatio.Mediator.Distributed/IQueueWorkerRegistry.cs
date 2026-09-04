namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Provides read-only access to registered queue workers and their runtime statistics.
/// </summary>
public interface IQueueWorkerRegistry
{
    /// <summary>
    /// Gets all registered queue workers.
    /// </summary>
    IReadOnlyList<QueueWorkerInfo> GetWorkers();

    /// <summary>
    /// Gets the worker info for a specific queue, or <c>null</c> if not found.
    /// </summary>
    QueueWorkerInfo? GetWorker(string queueName);
}
