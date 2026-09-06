namespace Foundatio.Mediator;

/// <summary>Which complete middleware lifecycle runs for a handler with an enqueue dispatcher.</summary>
public enum MiddlewareStage
{
    /// <summary>Runs when processing the message. This is the default.</summary>
    Processing = 0,
    /// <summary>Runs around enqueueing, for caller-side validation and enrichment.</summary>
    Enqueue = 1,
    /// <summary>Runs separately during enqueueing and processing.</summary>
    Both = 2
}
