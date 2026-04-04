using Common.Module;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Aws;
using Microsoft.AspNetCore.Authentication.Cookies;
using Orders.Module;
using Products.Module;
using Reports.Module;
using Foundatio.Mediator.Distributed.Redis;
using Scalar.AspNetCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults (OpenTelemetry, health checks, service discovery)
// Works fine both with and without the Aspire AppHost
builder.AddServiceDefaults();

builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenApi();

// Simple cookie authentication for the sample
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ModularMonolith.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Return 401 JSON instead of redirecting to a login page
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// Add Foundatio.Mediator — all referenced module assemblies are auto-discovered
builder.Services.AddMediator();

// ── Redis + HybridCache (L1 in-memory + L2 Redis distributed cache) ──
var redisConnection = builder.Configuration.GetConnectionString("redis");
if (!string.IsNullOrEmpty(redisConnection))
{
    // Register IConnectionMultiplexer for repository persistence
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect(redisConnection));

    // Register IDistributedCache backed by Redis (L2 for HybridCache)
    builder.Services.AddStackExchangeRedisCache(options =>
        options.Configuration = redisConnection);

    // Use Redis for queue job state tracking (shared across all replicas)
    builder.Services.AddMediatorRedisJobStateStore();
}

// HybridCache provides L1 (in-memory) + L2 (distributed) caching.
// When Redis is configured, the L2 backs all nodes; without it, L1-only.
builder.Services.AddHybridCache();

// ── AWS SQS/SNS (only when running under Aspire with LocalStack or real AWS) ──
var awsServiceUrl = builder.Configuration["AWS:ServiceURL"];
if (!string.IsNullOrEmpty(awsServiceUrl))
{
    // These usings are only needed in the SQS codepath - import dynamically
    // to keep the standalone path clean
    ConfigureAwsDistributed(builder.Services, awsServiceUrl);
}

// Wire up distributed infrastructure (falls back to in-memory when no SQS/SNS is registered)
builder.Services.AddMediatorDistributed();
builder.Services.AddMediatorDistributedNotifications();

// Add module services
// Order matters: Common.Module provides cross-cutting services that other modules may depend on
builder.Services.AddCommonModule();
builder.Services.AddOrdersModule();
builder.Services.AddProductsModule();
builder.Services.AddReportsModule();

var app = builder.Build();

// Health check endpoints
app.MapDefaultEndpoints();

// Serve static files from the SPA
app.UseDefaultFiles();
app.MapStaticAssets();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Map module endpoints - discovers and maps all endpoint modules from referenced assemblies
app.MapMediatorEndpoints();

// SPA fallback - serves index.html for client-side routing
app.MapFallbackToFile("/index.html");

app.Run();

// ── Extracted so AWS SDK types are only referenced when AWS:ServiceURL is set ──
static void ConfigureAwsDistributed(IServiceCollection services, string serviceUrl)
{
    // LocalStack doesn't require real credentials — use dummy ones to bypass
    // the default credential chain which fails without AWS config/env vars
    var credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test");

    var sqsConfig = new Amazon.SQS.AmazonSQSConfig
    {
        ServiceURL = serviceUrl,
        AuthenticationRegion = "us-east-1"
    };
    services.AddSingleton<Amazon.SQS.IAmazonSQS>(_ => new Amazon.SQS.AmazonSQSClient(credentials, sqsConfig));

    var snsConfig = new Amazon.SimpleNotificationService.AmazonSimpleNotificationServiceConfig
    {
        ServiceURL = serviceUrl,
        AuthenticationRegion = "us-east-1"
    };
    services.AddSingleton<Amazon.SimpleNotificationService.IAmazonSimpleNotificationService>(
        _ => new Amazon.SimpleNotificationService.AmazonSimpleNotificationServiceClient(credentials, snsConfig));

    services.AddMediatorSqs();
    services.AddMediatorSnsSqsPubSub();
}
