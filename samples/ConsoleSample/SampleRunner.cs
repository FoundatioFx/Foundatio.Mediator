using ConsoleSample.Messages;
using Foundatio.Mediator;

namespace ConsoleSample;

public class SampleRunner
{
    private readonly IMediator _mediator;

    public SampleRunner(IMediator mediator, IServiceProvider serviceProvider)
    {
        _mediator = mediator;
    }

    public async Task RunAllSamplesAsync()
    {
        Console.WriteLine("🚀 Foundatio.Mediator Clean Sample");
        Console.WriteLine("===================================\n");

        await RunSimpleExamples();
        await RunOrderCrudExamples();
        await RunEventPublishingExamples();

        Console.WriteLine("\n🎉 All samples completed successfully!");
    }

    private async Task RunSimpleExamples()
    {
        Console.WriteLine("1️⃣ Simple Command and Query Examples");
        Console.WriteLine("=====================================\n");

        // Simple command (no response)
        await _mediator.InvokeAsync(new Ping("Hello from mediator!"));

        // Simple query (with response)
        var greeting = _mediator.Invoke<string>(new GetGreeting("World"));
        Console.WriteLine($"✨ {greeting}\n");
    }

    private async Task RunOrderCrudExamples()
    {
        Console.WriteLine("2️⃣ Order CRUD with Result Pattern");
        Console.WriteLine("==================================\n");

        // Create order
        Console.WriteLine("📝 Creating order...");
        var createResult = await _mediator.InvokeAsync<Result<Order>>(new CreateOrder("CUST-001", 299.99m, "Premium widget"));

        if (createResult.IsSuccess)
        {
            var order = createResult.Value;
            Console.WriteLine($"✅ Order created: {order.Id} for ${order.Amount:F2}\n");

            // Get order
            Console.WriteLine("🔍 Retrieving order...");
            var getResult = _mediator.Invoke<Result<Order>>(new GetOrder(order.Id));
            if (getResult.IsSuccess)
            {
                Console.WriteLine($"✅ Order found: {getResult.Value.Id} - {getResult.Value.Description}\n");
            }

            // Update order
            Console.WriteLine("📝 Updating order...");
            var updateResult = await _mediator.InvokeAsync<Result<Order>>(new UpdateOrder(order.Id, 399.99m, "Premium widget - Updated"));
            if (updateResult.IsSuccess)
            {
                Console.WriteLine($"✅ Order updated: ${updateResult.Value.Amount:F2}\n");
            }

            // Delete order
            Console.WriteLine("🗑️ Deleting order...");
            var deleteResult = await _mediator.InvokeAsync<Result>(new DeleteOrder(order.Id));
            if (deleteResult.IsSuccess)
            {
                Console.WriteLine($"✅ Order deleted successfully\n");
            }
        }
        else
        {
            Console.WriteLine($"❌ Failed to create order: {string.Join(", ", createResult.Errors)}\n");
        }

        // Demonstrate validation errors
        Console.WriteLine("🚫 Testing validation errors...");
        var invalidResult = await _mediator.InvokeAsync<Result<Order>>(new CreateOrder("", -100m, "Invalid order"));
        if (!invalidResult.IsSuccess)
        {
            Console.WriteLine($"❌ Validation failed as expected: {string.Join(", ", invalidResult.Errors)}\n");
        }
    }

    private async Task RunEventPublishingExamples()
    {
        Console.WriteLine("3️⃣ Event Publishing (Multiple Handlers)");
        Console.WriteLine("========================================\n");

        Console.WriteLine("📢 Publishing OrderCreated event (will trigger multiple handlers)...");
        await _mediator.PublishAsync(new OrderCreated("ORD-DEMO-001", "CUST-001", 199.99m, DateTime.UtcNow));

        Console.WriteLine("\n📢 Publishing OrderUpdated event...");
        await _mediator.PublishAsync(new OrderUpdated("ORD-DEMO-001", 249.99m, DateTime.UtcNow));

        Console.WriteLine("\n📢 Publishing OrderDeleted event...");
        await _mediator.PublishAsync(new OrderDeleted("ORD-DEMO-001", DateTime.UtcNow));

        Console.WriteLine();
    }
}
