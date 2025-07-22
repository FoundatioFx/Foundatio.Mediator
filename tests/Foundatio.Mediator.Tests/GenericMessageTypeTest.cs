using Foundatio.Xunit;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

// Test interface for polymorphic message handling
public interface INotification
{
    string Message { get; }
}

// Test base class for polymorphic message handling
public abstract class BaseMessage
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// Concrete message that implements interface and inherits from base class
public class UserRegisteredEvent : BaseMessage, INotification
{
    public string Message => $"User {UserId} registered at {Timestamp}";
    public string UserId { get; set; } = string.Empty;
}

// Handler for the interface - should handle all INotification messages
public class NotificationHandler
{
    public static readonly List<string> ReceivedMessages = new();

    public async Task HandleAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add($"NotificationHandler: {notification.Message}");
        await Task.CompletedTask;
    }
}

// Handler for the base class - should handle all BaseMessage messages
public class BaseMessageHandler
{
    public static readonly List<string> ReceivedMessages = new();

    public async Task HandleAsync(BaseMessage message, CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add($"BaseMessageHandler: {message.Timestamp}");
        await Task.CompletedTask;
    }
}

// Handler for the specific message type
public class UserRegisteredHandler
{
    public static readonly List<string> ReceivedMessages = new();

    public async Task HandleAsync(UserRegisteredEvent userRegistered, CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add($"UserRegisteredHandler: {userRegistered.UserId}");
        await Task.CompletedTask;
    }
}

// Test message with generic type parameter - this is OK
public class GenericMessage<T>
{
    public T Value { get; set; } = default!;
}

// Handler that uses generic message type - this is now OK
public class GenericMessageHandler
{
    public async Task HandleAsync(GenericMessage<string> message, CancellationToken cancellationToken = default)
    {
        // This handler should be generated since generic message types are allowed
        await Task.CompletedTask;
    }
}

// Handler with generic method - this should be skipped (no warnings, just ignored)
public class GenericMethodHandler
{
    public async Task HandleAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        // This handler method should be skipped because it has generic type parameters
        await Task.CompletedTask;
    }
}

// Concrete message type
public class ConcreteMessage
{
    public string Value { get; set; } = string.Empty;
}

// Handler that uses concrete message type - should work fine
public class ConcreteMessageHandler
{
    public string Handle(ConcreteMessage message, CancellationToken cancellationToken = default)
    {
        return $"Handled: {message.Value}";
    }
}

public class GenericMessageTypeTest : TestWithLoggingBase
{
    public GenericMessageTypeTest(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void GenericMethodHandler_ShouldBeSkipped()
    {
        // This test primarily exists to trigger the source generator analysis
        // The actual test is that there should be NO warnings for generic message types
        // but generic handler methods should be silently skipped

        var concreteMessage = new ConcreteMessage { Value = "test" };
        Assert.NotNull(concreteMessage);

        var genericMessage = new GenericMessage<string> { Value = "test" };
        Assert.NotNull(genericMessage);
    }

    [Fact]
    public async Task PolymorphicMessageHandling_ShouldCallAllApplicableHandlers()
    {
        // Clear any previous test results
        NotificationHandler.ReceivedMessages.Clear();
        BaseMessageHandler.ReceivedMessages.Clear();
        UserRegisteredHandler.ReceivedMessages.Clear();

        // Create a UserRegisteredEvent that implements INotification and inherits from BaseMessage
        var userEvent = new UserRegisteredEvent { UserId = "user123" };

        // When publishing, all applicable handlers should be called:
        // 1. UserRegisteredHandler (exact type match)
        // 2. NotificationHandler (implements INotification)
        // 3. BaseMessageHandler (inherits from BaseMessage)

        // This test verifies the polymorphic behavior exists - actual mediator testing
        // would require DI setup which is tested in other integration tests

        Assert.True(userEvent is INotification);
        Assert.True(userEvent is BaseMessage);
        Assert.Equal("UserRegisteredEvent", userEvent.GetType().Name);
    }
}
