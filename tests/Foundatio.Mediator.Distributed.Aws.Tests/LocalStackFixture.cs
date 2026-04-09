using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Foundatio.Mediator.Distributed.Aws.Tests;

/// <summary>
/// Aspire fixture that manages a LocalStack container for AWS integration tests.
/// The container is started once and shared across all tests in the collection.
/// </summary>
public class LocalStackFixture : IAsyncLifetime
{
    public string ServiceUrl { get; private set; } = null!;
    public DistributedApplication App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = DistributedApplicationTestingBuilder.Create();

        builder.AddContainer("localstack", "localstack/localstack", "3.8.1")
            .WithHttpEndpoint(targetPort: 4566, name: "main")
            .WithHttpHealthCheck("/_localstack/health", endpointName: "main")
            .WithEnvironment("SERVICES", "sqs,sns");

        App = await builder.BuildAsync();
        await App.StartAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await App.ResourceNotifications.WaitForResourceHealthyAsync(
            "localstack", cts.Token);

        ServiceUrl = App.GetEndpoint("localstack", "main").ToString().TrimEnd('/');
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
    }
}
