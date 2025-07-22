using Foundatio.Xunit;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

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
    public async Task<string> HandleAsync(ConcreteMessage message, CancellationToken cancellationToken = default)
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
}
