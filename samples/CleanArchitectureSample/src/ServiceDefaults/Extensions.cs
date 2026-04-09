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
                        // Drop the root HTTP span for noisy polling endpoints.
                        // The SuppressInstrumentation middleware in Program.cs prevents child spans.
                        o.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/api/events")
                            && ctx.Request.Path != "/api/queues/queues"
                            && ctx.Request.Path != "/api/queues/job-dashboard";
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
                    .AddSource("Foundatio.Mediator")
                    .AddRedisInstrumentation();

                // Drop noisy background spans:
                // - SQS polling from queue workers
                // - Orphaned Redis spans from job state store operations (keep Redis spans that are
                //   children of an application trace, drop root-level infrastructure noise)
                tracing.AddProcessor(new FilteringProcessor(activity =>
                    activity.OperationName is not "SQS.ReceiveMessage" and not "SQS.DeleteMessage"
                    && !(activity.Source.Name == "OpenTelemetry.Instrumentation.StackExchangeRedis" && activity.Parent is null)));
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

    /// <summary>
    /// Suppresses all OpenTelemetry instrumentation for requests matching the given paths.
    /// No activities (spans) are created for the request or any downstream calls (Redis, SQS, etc.).
    /// </summary>
    public static WebApplication UseSuppressInstrumentation(this WebApplication app, params string[] pathPrefixes)
    {
        app.Use(async (context, next) =>
        {
            foreach (var prefix in pathPrefixes)
            {
                if (context.Request.Path.StartsWithSegments(prefix))
                {
                    using (SuppressInstrumentationScope.Begin())
                    {
                        await next();
                    }
                    return;
                }
            }

            await next();
        });

        return app;
    }
}

/// <summary>
/// Drops activities that match the predicate so they are never exported.
/// Used for background worker spans (SQS polling) that aren't HTTP requests.
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
