using Foundatio.Mediator;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests
{

    public class MassTransitExclusionTest : TestWithLoggingBase
    {
        public MassTransitExclusionTest(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task Should_Exclude_MassTransit_Consume_Methods()
        {
            var services = new ServiceCollection();
            services.AddMediator();

            var serviceProvider = services.BuildServiceProvider();
            var mediator = serviceProvider.GetRequiredService<IMediator>();

            // This should work - it's a normal Foundatio handler
            string result = await mediator.InvokeAsync<string>(new TestMessageForMassTransitExclusion { Value = "test" });
            Assert.Equal("Foundatio Handler: test", result);

            // Verify that MassTransit handlers are not registered
            // Check for our Foundatio handler specifically
            var foundatioHandlers = serviceProvider.GetKeyedServices<HandlerRegistration>(
                "Foundatio.Mediator.Tests.TestMessageForMassTransitExclusion").ToList();
            _logger.LogInformation("Found {Count} handlers for TestMessageForMassTransitExclusion", foundatioHandlers.Count);
            Assert.Single(foundatioHandlers);

            // Check that MassTransit message handlers are NOT registered
            var massTransitHandlers = serviceProvider.GetKeyedServices<HandlerRegistration>(
                "Foundatio.Mediator.Tests.MassTransitMessage").ToList();
            _logger.LogInformation("Found {Count} handlers for MassTransitMessage", massTransitHandlers.Count);
            Assert.Empty(massTransitHandlers);
        }
    }


    // Test message for Foundatio handler
    public record TestMessageForMassTransitExclusion { public string Value { get; init; } = String.Empty; }

    // Test message that would be used by MassTransit (but we won't register a MassTransit handler here for simplicity)
    public record MassTransitMessage { public string Value { get; init; } = String.Empty; }

    // Normal Foundatio handler - should be picked up
    public class TestMessageForMassTransitExclusionHandler
    {
        public async Task<string> HandleAsync(TestMessageForMassTransitExclusion message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(1, cancellationToken);
            return $"Foundatio Handler: {message.Value}";
        }
    }

    // MassTransit-style handler - should be excluded due to ConsumeContext parameter
    public class TestMassTransitStyleHandler
    {
        // This method should be excluded because it follows MassTransit pattern
        public async Task Consume(MassTransit.ConsumeContext<MassTransitMessage> context)
        {
            await Task.CompletedTask;
            // This handler should never be registered by Foundatio
        }
    }
}

// Mock ConsumeContext for testing (we don't want to bring in the full MassTransit dependency just for tests)
namespace MassTransit
{
    public interface ConsumeContext<T>
    {
        T Message { get; }
        CancellationToken CancellationToken { get; }
    }
}
// Force rebuild
