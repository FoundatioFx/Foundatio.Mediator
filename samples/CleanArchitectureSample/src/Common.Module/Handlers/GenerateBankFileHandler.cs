using Common.Module.Events;
using Common.Module.Messages;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// Money movement must never run twice at once. <c>[QueueLock]</c> takes a distributed lock on the message's
/// <see cref="GenerateBankFile.LockKey"/> before the handler runs; a second message for the same bank that
/// arrives while the lock is held is completed without running, on this worker or any other.
/// </summary>
[Queue(Group = "exports", Concurrency = 2, TimeoutSeconds = 60, Description = "Bank file generation; [QueueLock] allows one run per bank at a time")]
[QueueLock]
public class GenerateBankFileHandler(HostInfo host, ILogger<GenerateBankFileHandler> logger)
{
    [HandlerEndpoint(Exclude = true)]
    public async Task<Result> HandleAsync(GenerateBankFile message, QueueContext queueContext, IMediator mediator, CancellationToken ct)
    {
        logger.LogInformation("Generating bank file for {Bank} (batch {BatchId}) on {HostId}", message.Bank, message.BatchId, host.HostId);

        for (int second = 1; second <= 4; second++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            await queueContext.ReportProgressAsync(ct).ConfigureAwait(false);
        }

        var fileName = $"{message.Bank}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{message.BatchId[..Math.Min(8, message.BatchId.Length)]}.ach";
        logger.LogInformation("Bank file {FileName} generated on {HostId}", fileName, host.HostId);

        await mediator.PublishAsync(new BankFileGenerated(message.Bank, fileName, host.HostId), ct).ConfigureAwait(false);
        return Result.Ok();
    }
}
