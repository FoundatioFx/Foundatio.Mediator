using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Foundatio.Mediator;

namespace SimpleTest;

public record TestMessage(string Content);

public class TestHandler 
{
    public void Handle(TestMessage message) 
    {
        Console.WriteLine($"Handling: {message.Content}");
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddMediator();
        services.AddTransient<TestHandler>();
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        
        try 
        {
            await mediator.InvokeAsync(new TestMessage("Hello World"));
            Console.WriteLine("Success!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}