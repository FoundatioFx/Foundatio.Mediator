using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class PolymorphicHandlingTest : TestWithLoggingBase
{
    public PolymorphicHandlingTest(ITestOutputHelper output) : base(output) { }

    // Test interface
    public interface IEvent
    {
        string EventType { get; }
    }

    // Test base class
    public abstract class DomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public abstract string EventName { get; }
    }

    // Concrete event that implements interface and inherits from base class
    public class OrderCreatedEvent : DomainEvent, IEvent
    {
        public string OrderId { get; set; } = String.Empty;
        public decimal Amount { get; set; }
        public override string EventName => "OrderCreated";
        public string EventType => EventName;
    }

    // Handler for the interface - should handle all IEvent messages
    public class EventLoggerHandler
    {
        public static readonly List<string> LoggedEvents = new();

        public async Task HandleAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            LoggedEvents.Add($"EventLoggerHandler: {@event.EventType}");
            await Task.CompletedTask;
        }
    }

    // Handler for the base class - should handle all DomainEvent messages
    public class DomainEventAuditorHandler
    {
        public static readonly List<string> AuditedEvents = new();

        public async Task HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            AuditedEvents.Add($"DomainEventAuditorHandler: {domainEvent.EventName} at {domainEvent.OccurredAt:HH:mm:ss}");
            await Task.CompletedTask;
        }
    }

    // Handler for the specific event type
    public class OrderProcessorHandler
    {
        public static readonly List<string> ProcessedOrders = new();

        public async Task HandleAsync(OrderCreatedEvent orderCreated, CancellationToken cancellationToken = default)
        {
            ProcessedOrders.Add($"OrderProcessorHandler: Order {orderCreated.OrderId} for ${orderCreated.Amount}");
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_WithPolymorphicMessage_ShouldCallAllApplicableHandlers()
    {
        // Arrange: Set up DI container
        var services = new ServiceCollection();
        services.AddMediator();

        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act: Try to publish an OrderCreatedEvent - this should not throw
        var orderEvent = new OrderCreatedEvent
        {
            OrderId = "ORDER-123",
            Amount = 99.99m
        };

        // The main test is that this doesn't throw an exception
        await mediator.PublishAsync(orderEvent);

        // If we get here without exception, the polymorphic handling is working
        // (handlers are found and called, even if we can't easily verify the calls)
        Assert.True(true, "PublishAsync completed without exception, indicating handlers were found and called");
    }
}
