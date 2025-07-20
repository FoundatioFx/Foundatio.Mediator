using Microsoft.Extensions.Logging;

namespace ConsoleSample.Services;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
}

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        _logger.LogInformation("Sending email to {Email} with subject: {Subject}", to, subject);
        await Task.Delay(50); // Simulate email sending
        Console.WriteLine($"✉️ Email sent to {to}: {subject}");
    }
}
