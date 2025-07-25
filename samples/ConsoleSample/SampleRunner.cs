using ConsoleSample.Messages;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace ConsoleSample;

public class SampleRunner
{
    private readonly IMediator _mediator;
    private readonly IServiceProvider _serviceProvider;

    public SampleRunner(IMediator mediator, IServiceProvider serviceProvider)
    {
        _mediator = mediator;
        _serviceProvider = serviceProvider;
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
        await RunValidationSamplesAsync();

        Console.WriteLine("🎉 All samples completed successfully!");
    }

    private async Task RunSimpleCommandQuerySamples()
    {
        Console.WriteLine("1️⃣ Testing Simple Command and Query...\n");

        // Test simple command (no response)
        await _mediator.InvokeAsync(new PingCommand("123"));

        // Test query with response
        string greeting = _mediator.Invoke<string>(new GreetingQuery("World"));
        Console.WriteLine($"Greeting response: {greeting}");

        Console.WriteLine("✅ Simple command/query test completed!\n");
    }

    private async Task RunDependencyInjectionSamples()
    {
        Console.WriteLine("2️⃣ Testing Dependency Injection in Handlers...\n");

        await _mediator.InvokeAsync(new SendWelcomeEmailCommand("john@example.com", "John Doe"));

        string personalizedGreeting = await _mediator.InvokeAsync<string>(new CreatePersonalizedGreetingQuery("Alice"));
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

        var uniqueCommand = new CreateOrder("ORD-001", "CUST-123", 299.99m, "Wireless Headphones");
        string processResult = await _mediator.InvokeAsync<string>(uniqueCommand);
        Console.WriteLine($"Process result: {processResult}");

        Console.WriteLine("✅ Single handler invoke test completed!\n");
    }

    private async Task RunMixedSyncAsyncSamples()
    {
        Console.WriteLine("5️⃣ Testing Mixed Sync/Async Handlers...\n");

        // Sync handler call
        string syncResult = _mediator.Invoke<string>(new SyncCalculationQuery(10, 5));
        Console.WriteLine($"Sync calculation result: {syncResult}");

        // Async handler call
        string asyncResult = await _mediator.InvokeAsync<string>(new AsyncCalculationQuery(20, 3));
        Console.WriteLine($"Async calculation result: {asyncResult}");

        Console.WriteLine("✅ Mixed sync/async test completed!\n");
    }

    public async Task RunValidationSamplesAsync()
    {
        Console.WriteLine("\n🔍 === Validation Middleware Demonstration ===");
        Console.WriteLine("This sample shows how validation middleware integrates with Result types");
        Console.WriteLine("The middleware validates input before handlers run, returning validation errors as Results\n");

        var createUserCommand = new CreateUserCommand
        {
            Name = "Sample User",
            Email = "missing@example.com",
            Age = 35,
            PhoneNumber = "123-456-7890"
        };

        var userResult = await _mediator.InvokeAsync<Result<User>>(createUserCommand);
        Console.WriteLine($"✅ User created successfully: {userResult.Value.Name}");

        // asking for user will cause any errors to throw since the result type can't be implicitly converted to User
        var user = await _mediator.InvokeAsync<User>(createUserCommand);
        Console.WriteLine($"✅ User created successfully: {userResult.Value.Name}");

        createUserCommand.Email = "existing@example.com";

        userResult = await _mediator.InvokeAsync<Result<User>>(createUserCommand);
        Console.WriteLine($"❌ User creation failed: {userResult.Errors}");

        // asking for user will cause any errors to throw since the result type can't be implicitly converted to User
        try
        {
            user = await _mediator.InvokeAsync<User>(createUserCommand);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error creating user: {ex.Message}");
        }

        createUserCommand.Email = String.Empty;

        userResult = await _mediator.InvokeAsync<Result<User>>(createUserCommand);
        Console.WriteLine($"❌ User creation failed: {userResult.Errors}");

        // asking for user will cause any errors to throw since the result type can't be implicitly converted to User
        try
        {
            user = await _mediator.InvokeAsync<User>(createUserCommand);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error creating user: {ex.Message}");
        }
    }
}
