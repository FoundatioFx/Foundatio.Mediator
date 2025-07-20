using Microsoft.Extensions.Logging;

namespace ConsoleSample.Services;

public interface INotificationService
{
    Task SendAsync(string message);
}

public class EmailNotificationService : INotificationService
{
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(ILogger<EmailNotificationService> logger)
    {
        _logger = logger;
    }

    public async Task SendAsync(string message)
    {
        await Task.Delay(50); // Simulate async work
        _logger.LogInformation("📧 Email notification sent: {Message}", message);
    }
}

public class SmsNotificationService : INotificationService
{
    private readonly ILogger<SmsNotificationService> _logger;

    public SmsNotificationService(ILogger<SmsNotificationService> logger)
    {
        _logger = logger;
    }

    public async Task SendAsync(string message)
    {
        await Task.Delay(30); // Simulate async work
        _logger.LogInformation("📱 SMS notification sent: {Message}", message);
    }
}
