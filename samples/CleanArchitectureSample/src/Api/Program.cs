using Common.Module;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Aws;
using Foundatio.Mediator.Distributed.Redis;
using Microsoft.AspNetCore.RateLimiting;
using Orders.Module;
using Products.Module;
using Reports.Module;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var options = AppOptions.Parse(args);

builder.AddServiceDefaults();
builder.AddRedisAndCaching();

// ── Foundatio.Mediator ──
builder.Services.AddMediator()
    .AddDistributedQueues(opts =>
    {
        opts.WorkersEnabled = options.IsWorkerEnabled;
        if (options.Queues is { Count: > 0 })
            opts.Queues = options.Queues;
    })
    .AddDistributedNotifications()
    .UseAws(aws => aws.ServiceUrl = builder.Configuration["AWS:ServiceURL"]!)
    .UseRedisJobState();

// ── Domain modules ──
builder.Services.AddCommonModule();
builder.Services.AddOrdersModule();
builder.Services.AddProductsModule();
builder.Services.AddReportsModule();

if (options.IsApiEnabled)
{
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddOpenApi();
    builder.AddSampleAuthentication();

    // Rate limiting policies — applied to endpoints via the [EndpointRateLimiter] attribute
    builder.Services.AddRateLimiter(rateLimiter =>
    {
        rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Default policy: 10 requests per 10-second window
        rateLimiter.AddFixedWindowLimiter("default", limiter =>
        {
            limiter.PermitLimit = 10;
            limiter.Window = TimeSpan.FromSeconds(10);
            limiter.QueueLimit = 0;
        });

        // Strict policy: 3 requests per 30-second window (for write operations)
        rateLimiter.AddFixedWindowLimiter("strict", limiter =>
        {
            limiter.PermitLimit = 3;
            limiter.Window = TimeSpan.FromSeconds(30);
            limiter.QueueLimit = 0;
        });
    });
}

var app = builder.Build();

app.LogStartupDiagnostics(options);
app.MapHealthCheckEndpoints();
app.UseSuppressInstrumentation("/api/queues/queues", "/api/queues/job-dashboard", "/api/events");

if (options.IsApiEnabled)
{
    app.UseDefaultFiles();
    app.MapStaticAssets();

    app.MapOpenApi();
    app.MapScalarApiReference();

    app.UseHttpsRedirection();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapMediatorEndpoints();
    app.MapFallbackToFile("/index.html");
}

app.Run();
