using Common.Module.Events;
using Common.Module.Messages;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// Retry schedule and dead-lettering made deterministic: the delivery fails until the attempt number passes
/// <see cref="DeliverWebhook.FailTimes"/>. Three attempts with 1s and 3s between them, then the dead-letter queue.
/// </summary>
[Queue(Group = "events", TrackProgress = true, MaxAttempts = 3, RetryDelays = "1s,3s", Description = "Webhook delivery on a fixed retry schedule; dead-letters after three attempts")]
public class FlakyWebhookHandler(HostInfo host, ILogger<FlakyWebhookHandler> logger)
{
    [HandlerEndpoint(Exclude = true)]
    public async Task<Result> HandleAsync(DeliverWebhook message, QueueContext queueContext, IMediator mediator, CancellationToken ct)
    {
        // A content error is never going to succeed on retry, so a non-retryable status dead-letters it at once.
        if (!Uri.TryCreate(message.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Result.Invalid($"'{message.Url}' is not an absolute http(s) URL");

        await queueContext.ReportProgressAsync(25, $"Attempt {queueContext.DequeueCount} on {host.HostId}", ct).ConfigureAwait(false);
        await Task.Delay(500, ct).ConfigureAwait(false);
        // A replay models the operator fixing the remote service before retrying the unchanged payload.
        bool replayed = queueContext.Headers.ContainsKey(MessageHeaders.OriginalJobId);
        if (!replayed && queueContext.DequeueCount <= message.FailTimes)
        {
            logger.LogWarning("Webhook {Url} failed on attempt {Attempt} of {MaxAttempts} on {HostId}",
                message.Url, queueContext.DequeueCount, queueContext.MaxAttempts, host.HostId);
            return Result.Error($"{uri.Host} returned 503 on attempt {queueContext.DequeueCount}");
        }

        logger.LogInformation("Webhook {Url} delivered on attempt {Attempt} by {HostId}", message.Url, queueContext.DequeueCount, host.HostId);

        await mediator.PublishAsync(new WebhookDelivered(message.Url, queueContext.DequeueCount, host.HostId, queueContext.JobId ?? queueContext.MessageId, queueContext.QueueName), ct).ConfigureAwait(false);
        return Result.Ok();
    }
}
