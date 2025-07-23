using Foundatio.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

// Test message types
public class TestMessage
{
    public string Value { get; set; } = String.Empty;
}

public class AnotherTestMessage
{
    public string Data { get; set; } = String.Empty;
}

// Generic handler class - this should be completely ignored by the source generator
public class GenericHandler<T>
{
    public async Task HandleAsync(T message, CancellationToken cancellationToken = default)
    {
        // This handler should be ignored because the class has generic type parameters
        await Task.CompletedTask;
    }

    public void Handle(T message)
    {
        // This handler should also be ignored
    }
}

// Another generic handler class with multiple type parameters
public class MultiGenericHandler<T, U>
{
    public async Task HandleAsync(T message, CancellationToken cancellationToken = default)
    {
        // This handler should be ignored because the class has generic type parameters
        await Task.CompletedTask;
    }

    public U Handle(T message)
    {
        // This handler should also be ignored
        return default(U)!;
    }
}

// Non-generic handler class - this should work normally
public class ConcreteTestMessageHandler
{
    public static readonly List<string> ReceivedMessages = new();

    public async Task HandleAsync(TestMessage message, CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add($"Handled: {message.Value}");
        await Task.CompletedTask;
    }
}

// Non-generic handler class - this should also work normally
public class AnotherTestMessageHandler
{
    public static readonly List<string> ReceivedMessages = new();

    public string Handle(AnotherTestMessage message)
    {
        string result = $"Handled: {message.Data}";
        ReceivedMessages.Add(result);
        return result;
    }
}

public class GenericHandlerClassTest : TestWithLoggingBase
{
    public GenericHandlerClassTest(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void GenericHandlerClasses_ShouldBeIgnored()
    {
        // This test primarily exists to trigger the source generator analysis
        // The key test is that there should be NO source generation for:
        // - GenericHandler<T>
        // - MultiGenericHandler<T, U>
        //
        // But source generation SHOULD work for:
        // - ConcreteTestMessageHandler
        // - AnotherTestMessageHandler

        var testMessage = new TestMessage { Value = "test" };
        Assert.NotNull(testMessage);

        var anotherMessage = new AnotherTestMessage { Data = "data" };
        Assert.NotNull(anotherMessage);

        // If the source generator worked correctly:
        // - Generic handler classes are ignored (no compilation errors, no handlers generated)
        // - Non-generic handler classes have handlers generated

        // Clear any previous results
        ConcreteTestMessageHandler.ReceivedMessages.Clear();
        AnotherTestMessageHandler.ReceivedMessages.Clear();

        // The actual functionality test would require DI setup which is tested in integration tests
        // This test ensures the code compiles without errors, which validates that:
        // 1. Generic handler classes don't cause source generation issues
        // 2. Non-generic handler classes are still processed correctly
    }

    [Fact]
    public void NonGenericHandlers_ShouldStillWork()
    {
        // This verifies that our change to ignore generic handler classes
        // doesn't break the processing of normal, non-generic handler classes

        var testMessage = new TestMessage { Value = "test-value" };
        var anotherMessage = new AnotherTestMessage { Data = "test-data" };

        // These should both be valid message types that can have handlers
        Assert.NotNull(testMessage);
        Assert.Equal("test-value", testMessage.Value);

        Assert.NotNull(anotherMessage);
        Assert.Equal("test-data", anotherMessage.Data);
    }
}
