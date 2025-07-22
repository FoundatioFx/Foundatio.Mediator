using ConsoleSample.Messages;
using Foundatio.Mediator;

namespace ConsoleSample;

public class SampleRunner
{
    private readonly IMediator _mediator;

    public SampleRunner(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task RunAllSamplesAsync()
    {
        Console.WriteLine("🚀 Foundatio.Mediator Comprehensive Sample");
        Console.WriteLine("==========================================\n");

        await RunSimpleCommandQuerySamples();
        await RunDependencyInjectionSamples();
        await RunPublishSamples();
        await RunSingleHandlerInvokeSamples();
        await RunMixedSyncAsyncSamples();

        Console.WriteLine("🎉 All samples completed successfully!");
    }

    private async Task RunSimpleCommandQuerySamples()
    {
        Console.WriteLine("1️⃣ Testing Simple Command and Query...\n");

        // Test simple command (no response)
        await _mediator.InvokeAsync(new PingCommand("123"));

        // Test query with response
        var greeting = await _mediator.InvokeAsync<string>(new GreetingQuery("World"));
        Console.WriteLine($"Greeting response: {greeting}");

        Console.WriteLine("✅ Simple command/query test completed!\n");
    }

    private async Task RunDependencyInjectionSamples()
    {
        Console.WriteLine("2️⃣ Testing Dependency Injection in Handlers...\n");

        await _mediator.InvokeAsync(new SendWelcomeEmailCommand("john@example.com", "John Doe"));

        var personalizedGreeting = await _mediator.InvokeAsync<string>(new CreatePersonalizedGreetingQuery("Alice"));
        Console.WriteLine($"Personalized greeting: {personalizedGreeting}");

        Console.WriteLine("✅ Dependency injection test completed!\n");
    }

    private async Task RunPublishSamples()
    {
        Console.WriteLine("3️⃣ Testing Publish with Multiple Handlers...\n");

        var orderEvent = new OrderCreatedEvent(
            OrderId: "ORD-001",
            CustomerId: "CUST-123",
            Amount: 299.99m,
            ProductName: "Wireless Headphones"
        );

        Console.WriteLine("📢 Publishing OrderCreatedEvent (should trigger multiple handlers)...");
        await _mediator.PublishAsync(orderEvent);

        Console.WriteLine("✅ Publish completed - all handlers were called!\n");
    }

    private async Task RunSingleHandlerInvokeSamples()
    {
        Console.WriteLine("4️⃣ Testing Invoke with Single Handler...\n");

        var uniqueCommand = new ProcessOrderCommand("ORD-001", "VIP Processing");
        var processResult = await _mediator.InvokeAsync<string>(uniqueCommand);
        Console.WriteLine($"Process result: {processResult}");

        Console.WriteLine("✅ Single handler invoke test completed!\n");
    }

    private async Task RunMixedSyncAsyncSamples()
    {
        Console.WriteLine("5️⃣ Testing Mixed Sync/Async Handlers...\n");

        // Sync handler call
        var syncResult = await _mediator.InvokeAsync<string>(new SyncCalculationQuery(10, 5));
        Console.WriteLine($"Sync calculation result: {syncResult}");

        // Async handler call
        var asyncResult = await _mediator.InvokeAsync<string>(new AsyncCalculationQuery(20, 3));
        Console.WriteLine($"Async calculation result: {asyncResult}");

        Console.WriteLine("✅ Mixed sync/async test completed!\n");
    }
}
