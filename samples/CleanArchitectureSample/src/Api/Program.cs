using Api.Infrastructure;
using Common.Module;
using Common.Module.Events;
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

// Created up front so the job metadata provider below can read the current request before the container exists.
var httpContextAccessor = new HttpContextAccessor();
builder.Services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);

// Handlers take TenantContext as a parameter: on a worker it comes from the message headers via the
// CallContext; inline, DI resolves it from the current request.
builder.Services.AddScoped(_ => TenantHeaderProvider.Resolve(httpContextAccessor.HttpContext));

// [QueueLock] needs a lock every replica shares; the library only supplies a process-local one for in-memory queues.
builder.Services.AddSingleton<IQueueLockProvider, RedisQueueLockProvider>();

// ── Foundatio.Mediator ──
builder.Services.AddMediator()
    .AddDistributedQueues(opts =>
    {
        // One setting decides which workers this process runs: "all", "none" (API node), or a list of
        // groups/queues such as "exports,imports". Comes from --workers, then Distributed:Workers config.
        opts.Workers = WorkerSelection.Parse(options.Workers ?? builder.Configuration["Distributed:Workers"]);

        // Tracked jobs remember who asked for them; the dashboard shows tenant and user per job.
        opts.JobMetadataProvider = _ => TenantHeaderProvider.JobMetadata(httpContextAccessor.HttpContext);
    })
    .AddQueueHeaderProvider<TenantHeaderProvider>()
    .AddDistributedNotifications(notifications => notifications
        // Every domain event in Common.Module crosses the bus (the event feed may be connected to any API node)...
        .IncludeNotificationsFromAssemblyOf<IOrderEvent>()
        // ...except this one: only queued handlers consume it, and the publishing node already enqueued them.
        .Exclude<ProductStockChanged>())
    .UseAws(aws => aws.ServiceUrl = builder.Configuration["AWS:ServiceURL"]!)
    .UseRedisJobState();

// ── Domain modules ──
builder.Services.AddCommonModule();
builder.Services.AddOrdersModule();
builder.Services.AddProductsModule();
builder.Services.AddReportsModule();

if (options.IsApiEnabled)
{
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
app.UseSuppressInstrumentation("/api/queues/queues", "/api/queues/queue", "/api/queues/job-dashboard", "/api/queues/dead-letters", "/api/queues/host", "/api/events");

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
