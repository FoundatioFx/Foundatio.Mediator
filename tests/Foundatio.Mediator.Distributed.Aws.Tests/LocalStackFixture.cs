using System.ComponentModel;
using System.Diagnostics;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Foundatio.Mediator.Distributed.Aws.Tests;

/// <summary>
/// Aspire fixture that manages one LocalStack container for every AWS test class in the
/// <see cref="LocalStackCollection"/>. When no container runtime is reachable the fixture
/// records a skip reason instead of failing, and <see cref="CreateSqsClient"/> /
/// <see cref="CreateSnsClient"/> skip the calling test.
/// </summary>
public class LocalStackFixture : IAsyncLifetime
{
    private static readonly TimeSpan s_runtimeProbeTimeout = TimeSpan.FromSeconds(15);

    public string ServiceUrl { get; private set; } = null!;
    public DistributedApplication? App { get; private set; }

    /// <summary>
    /// Why the LocalStack container could not be started, or <c>null</c> when it is running.
    /// </summary>
    public string? SkipReason { get; private set; }

    public bool IsAvailable => SkipReason is null;

    public async ValueTask InitializeAsync()
    {
        SkipReason = await ProbeContainerRuntimeAsync();
        if (SkipReason is not null)
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                throw new InvalidOperationException($"AWS integration tests require a reachable container runtime in CI: {SkipReason}");
            return;
        }

        var builder = DistributedApplicationTestingBuilder.Create();

        builder.AddContainer("localstack", "localstack/localstack", "3.8.1")
            .WithHttpEndpoint(targetPort: 4566, name: "main")
            .WithHttpHealthCheck("/_localstack/health", endpointName: "main")
            .WithEnvironment("SERVICES", "sqs,sns")
            // Emulate the real 60-second re-creation block so the stable-host restart path is exercised.
            .WithEnvironment("SQS_DELAY_RECENTLY_DELETED", "1");

        App = await builder.BuildAsync();
        await App.StartAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await App.ResourceNotifications.WaitForResourceHealthyAsync("localstack", cts.Token);

        ServiceUrl = App.GetEndpoint("localstack", "main").ToString().TrimEnd('/');
    }

    public IAmazonSQS CreateSqsClient()
    {
        Assert.SkipWhen(!IsAvailable, SkipReason ?? string.Empty);
        return new AmazonSQSClient(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig { ServiceURL = ServiceUrl });
    }

    public IAmazonSimpleNotificationService CreateSnsClient()
    {
        Assert.SkipWhen(!IsAvailable, SkipReason ?? string.Empty);
        return new AmazonSimpleNotificationServiceClient(new BasicAWSCredentials("test", "test"), new AmazonSimpleNotificationServiceConfig { ServiceURL = ServiceUrl });
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
    }

    private static async Task<string?> ProbeContainerRuntimeAsync()
    {
        var runtime = Environment.GetEnvironmentVariable("DOTNET_ASPIRE_CONTAINER_RUNTIME");
        if (string.IsNullOrWhiteSpace(runtime))
            runtime = "docker";

        try
        {
            using var process = Process.Start(new ProcessStartInfo(runtime, "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process is null)
                return $"LocalStack tests skipped: '{runtime}' could not be started.";

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(s_runtimeProbeTimeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return $"LocalStack tests skipped: '{runtime} info' did not respond within {s_runtimeProbeTimeout.TotalSeconds:0} seconds.";
            }

            await stdout;
            if (process.ExitCode != 0)
                return $"LocalStack tests skipped: '{runtime} info' exited with {process.ExitCode}: {(await stderr).Trim()}";

            return null;
        }
        catch (Win32Exception)
        {
            return $"LocalStack tests skipped: '{runtime}' is not installed.";
        }
    }
}

[CollectionDefinition(nameof(LocalStackCollection))]
public class LocalStackCollection : ICollectionFixture<LocalStackFixture>;
