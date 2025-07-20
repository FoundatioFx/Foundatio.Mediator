using Microsoft.Extensions.Logging;

namespace ConsoleSample.Services;

public interface IAuditService
{
    Task LogEventAsync(string eventType, object eventData);
}

public class AuditService : IAuditService
{
    private readonly ILogger<AuditService> _logger;

    public AuditService(ILogger<AuditService> logger)
    {
        _logger = logger;
    }

    public async Task LogEventAsync(string eventType, object eventData)
    {
        await Task.Delay(20); // Simulate async work
        _logger.LogInformation("📋 Audit logged: {EventType} - {EventData}", eventType, eventData);
    }
}
