using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Distributed;

/// <summary>Confirms transport acceptance, not completion. JobId is null when tracking is disabled.</summary>
public sealed record QueueReceipt(string QueueName, string? JobId);

/// <summary>An enqueue failed without confirming whether the transport accepted it. Inspect tracked state before retrying.</summary>
public sealed class QueueEnqueueException(QueueReceipt receipt, Exception innerException)
    : Exception($"Acceptance of a message for '{receipt.QueueName}' could not be confirmed.", innerException)
{
    /// <summary>The attempted destination and job identity, for reconciliation.</summary>
    public QueueReceipt Receipt { get; } = receipt;
}

internal sealed class QueueReceiptCapture
{
    public QueueReceipt? Receipt { get; set; }
}

/// <summary>Explicit enqueue operations that preserve validation results and expose job identity.</summary>
public static class QueueMediatorExtensions
{
    /// <summary>
    /// Runs the selected queue handler's enqueue pipeline and returns its receipt. Validation may return
    /// an unsuccessful Result without sending. Like Invoke, this requires exactly one matching handler.
    /// For multiple independent subscriptions use PublishAsync. This does not wait for processing.
    /// </summary>
    public static async ValueTask<Result<QueueReceipt>> EnqueueAsync(this IMediator mediator, object message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (mediator is not IServiceProvider services)
            throw new InvalidOperationException("EnqueueAsync requires a mediator that exposes its invocation service provider.");
        var registry = services.GetRequiredService<HandlerRegistry>();
        var handler = services.GetRequiredService<QueueTopology>().GetEnqueueHandler(message.GetType(), registry);
        var capture = new QueueReceiptCapture();
        using var context = CallContext.Rent().Set(capture);
        var result = await handler.HandleAsync(mediator, message, context, cancellationToken, typeof(object)).ConfigureAwait(false);
        if (capture.Receipt is { } receipt)
            return receipt;
        if (result is IResult validation && !validation.IsSuccess)
            return Result<QueueReceipt>.FromResult(validation);
        throw new InvalidOperationException("The enqueue pipeline returned without sending a message. Enqueue validation should return an unsuccessful Result when it short-circuits.");
    }
}
