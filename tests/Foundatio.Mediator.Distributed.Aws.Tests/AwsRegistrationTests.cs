using Foundatio.Mediator.Distributed.Aws;
using Foundatio.Mediator.Distributed.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed.Aws.Tests;

public class AwsRegistrationTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    public async Task OptionalFeatures_ResolveInEitherRegistrationOrder(bool queues, bool notifications, bool transportFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddMediator(options => options.AddAssembly<QueuedCommandHandler>());
        if (transportFirst)
            builder.UseAws(options => options.ServiceUrl = "http://127.0.0.1:4566");
        if (queues)
            builder.AddDistributedQueues(options => options.Workers = WorkerSelection.None);
        if (notifications)
            builder.AddDistributedNotifications();
        if (!transportFirst)
            builder.UseAws(options => options.ServiceUrl = "http://127.0.0.1:4566");

        await using var provider = services.BuildServiceProvider();
        // No network is necessary to construct optional services or their hosted workers.
        Assert.IsType<SqsQueueClient>(provider.GetRequiredService<IQueueClient>());
        Assert.IsType<SqsPubSubClient>(provider.GetRequiredService<IPubSubClient>());
        Assert.NotEmpty(provider.GetServices<IHostedService>());
        Assert.Equal(notifications, provider.GetService<DistributedNotificationOptions>() is not null);
    }
}
