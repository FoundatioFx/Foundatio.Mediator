namespace Foundatio.Mediator.Distributed;

/// <summary>Bounds infrastructure calls even when a provider fails to observe its token.</summary>
internal static class QueueOperation
{
    public static async Task RunAsync(Func<CancellationToken, Task> operation, TimeSpan timeout,
        TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        using var budget = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);
        await operation(linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
    }

    public static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout,
        TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        using var budget = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);
        return await operation(linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
    }
}
