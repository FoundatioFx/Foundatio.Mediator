using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class StaticHandlerTest : TestWithLoggingBase
{
    public StaticHandlerTest(ITestOutputHelper output) : base(output) { }

    public record StaticSyncCommandMessage(string Value);
    public record StaticAsyncCommandMessage(string Value);
    public record StaticSyncQueryMessage(string Input);
    public record StaticAsyncQueryMessage(string Input);
    public record StaticQueryWithDIMessage(string Input);

    public class StaticTestHandler
    {
        public static void Handle(StaticSyncCommandMessage message)
        {
            // Static command handler - no return value
        }

        public static async Task HandleAsync(StaticAsyncCommandMessage message, CancellationToken cancellationToken)
        {
            // Static async command handler
            await Task.CompletedTask;
        }

        public static string Handle(StaticSyncQueryMessage query)
        {
            // Static query handler with return value
            return $"Static response: {query.Input}";
        }

        public static async Task<string> HandleAsync(StaticAsyncQueryMessage query, CancellationToken cancellationToken)
        {
            // Static async query handler with return value
            await Task.CompletedTask;
            return $"Static async response: {query.Input}";
        }

        public static string Handle(StaticQueryWithDIMessage query, ILogger<StaticTestHandler> logger)
        {
            logger.LogInformation("Static handler with DI called with: {Input}", query.Input);
            return $"Static with DI: {query.Input}";
        }
    }

    [Fact]
    public async Task CanInvokeStaticSyncCommandHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Use sync call for sync handler
        mediator.Invoke(new StaticSyncCommandMessage("test"));
    }

    [Fact]
    public async Task CanInvokeStaticAsyncCommandHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // This should not throw since static handlers don't need DI resolution
        await mediator.InvokeAsync(new StaticAsyncCommandMessage("test"));
    }

    [Fact]
    public async Task CanInvokeStaticSyncQueryHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Use sync call for sync handler
        var result = mediator.Invoke<string>(new StaticSyncQueryMessage("test"));

        Assert.Equal("Static response: test", result);
    }

    [Fact]
    public async Task CanInvokeStaticAsyncQueryHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.InvokeAsync<string>(new StaticAsyncQueryMessage("test"));

        Assert.Equal("Static async response: test", result);
    }

    [Fact]
    public async Task StaticHandlerCanResolveDependenciesFromDI()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTestLogger());
        services.AddMediator();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = mediator.Invoke<string>(new StaticQueryWithDIMessage("test-di"));

        Assert.Equal("Static with DI: test-di", result);
    }
}
