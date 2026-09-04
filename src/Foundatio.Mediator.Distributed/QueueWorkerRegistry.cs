namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Default implementation of <see cref="IQueueWorkerRegistry"/>.
/// Populated during DI registration and updated at runtime by <see cref="QueueWorker"/> instances.
/// </summary>
internal sealed class QueueWorkerRegistry : IQueueWorkerRegistry
{
    private readonly List<QueueWorkerInfo> _workers = [];
    private readonly Dictionary<string, QueueWorkerInfo> _byQueueName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<QueueWorkerInfo> GetWorkers() => _workers;

    public QueueWorkerInfo? GetWorker(string queueName)
        => _byQueueName.GetValueOrDefault(queueName);

    internal void Register(QueueWorkerInfo info)
    {
        _workers.Add(info);
        _byQueueName[info.QueueName] = info;
    }
}
