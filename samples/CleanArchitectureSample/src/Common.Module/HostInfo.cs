namespace Common.Module;

/// <summary>
/// Identifies this process so events and job progress can say which replica did the work. Aspire sets
/// <c>OTEL_SERVICE_NAME</c> to the resource name (<c>api</c>, <c>worker-exports</c>, ...).
/// </summary>
public sealed class HostInfo
{
    public HostInfo()
    {
        var service = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");
        HostId = $"{(string.IsNullOrWhiteSpace(service) ? Environment.MachineName : service)}:{Environment.ProcessId}";
    }

    public string HostId { get; }
}
