namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Defines the retry delay strategy for failed queue messages.
/// </summary>
public enum QueueRetryPolicy
{
    /// <summary>
    /// No delay between retries. Failed messages are immediately redelivered.
    /// </summary>
    None,

    /// <summary>
    /// Constant delay between retries. Each retry waits the same configured base delay.
    /// </summary>
    Fixed,

    /// <summary>
    /// Exponential backoff between retries. Each successive retry doubles the delay.
    /// A proportional jitter (±10%) is added to prevent thundering herd.
    /// </summary>
    Exponential
}
