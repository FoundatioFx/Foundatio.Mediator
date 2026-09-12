using Foundatio.Mediator.Distributed;
using System.ComponentModel.DataAnnotations;

namespace Common.Module.Messages;

/// <summary>Simulated export: <paramref name="Steps"/> progress reports roughly <paramref name="StepDelayMs"/> apart.</summary>
public record DemoExportJob(
    [property: Range(1, 100)] int Steps = 20,
    [property: Range(50, 5000)] int StepDelayMs = 1500,
    [property: Range(0, 10)] int FailTimes = 0,
    bool CriticalFailure = false);

/// <summary>Simulated catalog import on the "imports" worker group.</summary>
public record ImportProductCatalog(int Rows = 200, int RowDelayMs = 50);

/// <summary>Webhook delivery that fails its first <paramref name="FailTimes"/> attempts.</summary>
public record DeliverWebhook(string Url, int FailTimes);

/// <summary>Bank file for one bank. Requests for the same bank share a lock key, so only one runs at a time.</summary>
public record GenerateBankFile(string Bank, string BatchId) : IHaveLockKey
{
    public string GetLockKey() => $"bank-file:{Bank}";
}
