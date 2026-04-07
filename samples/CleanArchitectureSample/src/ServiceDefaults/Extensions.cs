using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(o =>
                    {
                        o.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/api/events")
                            && !ctx.Request.Path.StartsWithSegments("/api/queues");
                    })
                    .AddHttpClientInstrumentation(o =>
                    {
                        // Filter out SQS long-polling (ReceiveMessage) to reduce trace noise
                        o.FilterHttpRequestMessage = req =>
                            req.Headers.TryGetValues("X-Amz-Target", out var values) != true
                            || !values.Any(v => v.Contains("ReceiveMessage", StringComparison.OrdinalIgnoreCase));
                    })
                    .AddAWSInstrumentation(o =>
                    {
                        o.SuppressDownstreamInstrumentation = true;
                    })
                    .AddSource("Foundatio.Mediator");

                // Drop noisy SQS polling/housekeeping spans from the AWS SDK instrumentation
                tracing.AddProcessor(new FilteringProcessor(activity =>
                    activity.OperationName is not "SQS.ReceiveMessage" and not "SQS.DeleteMessage"));
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}

/// <summary>
/// Drops activities that don't match the predicate so they are never exported.
/// </summary>
internal sealed class FilteringProcessor(Func<Activity, bool> predicate) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (!predicate(data))
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;

        base.OnEnd(data);
    }
}
