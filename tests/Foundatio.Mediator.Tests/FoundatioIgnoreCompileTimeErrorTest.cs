using Foundatio.Mediator;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

/// <summary>
/// This test file demonstrates that handlers marked with [FoundatioIgnore]
/// cannot be called, resulting in compile-time errors FMED004.
///
/// If you uncomment the code below, you will see compile-time errors proving
/// that the ignored handlers are not generated.
/// </summary>
public class FoundatioIgnoreCompileTimeErrorTest : TestWithLoggingBase
{
    public FoundatioIgnoreCompileTimeErrorTest(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Demonstration_Of_FoundatioIgnore_Preventing_Handler_Generation()
    {
        var services = new ServiceCollection();
        services.AddMediator();

        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // These lines would cause compile-time errors if uncommented:

        // ERROR FMED004: No handler found for message type 'Foundatio.Mediator.Tests.IgnoredClassMessage'
        // var result1 = await mediator.InvokeAsync<string>(new IgnoredClassMessage { Value = "test" });

        // ERROR FMED004: No handler found for message type 'Foundatio.Mediator.Tests.IgnoredMethodMessage'
        // var result2 = await mediator.InvokeAsync<string>(new IgnoredMethodMessage { Value = "test" });

        // This one works because it's NOT ignored:
        var result3 = mediator.InvokeAsync<string>(new ValidMethodMessage { Value = "test" });
    }
}
