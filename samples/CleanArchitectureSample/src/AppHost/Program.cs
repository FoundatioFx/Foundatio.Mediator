var builder = DistributedApplication.CreateBuilder(args);

// LocalStack provides SQS + SNS for local development
var localstack = builder.AddContainer("localstack", "localstack/localstack", "latest")
    .WithHttpEndpoint(targetPort: 4566, name: "main")
    .WithHttpHealthCheck("/_localstack/health", endpointName: "main")
    .WithEnvironment("SERVICES", "sqs,sns");

// Redis for shared persistence and distributed caching
var redis = builder.AddRedis("redis");

// API project — serves HTTP endpoints and the SPA frontend, but no queue workers.
// Queue messages are still enqueued to SQS; the worker resource below processes them.
var api = builder.AddProject<Projects.Api>("api")
    .WithHttpEndpoint()
    .WithHttpsEndpoint()
    .WithExternalHttpEndpoints()
    .WithReplicas(3)
    .WaitFor(localstack)
    .WaitFor(redis)
    .WithReference(localstack.GetEndpoint("main"))
    .WithReference(redis)
    .WithEnvironment("AWS__ServiceURL", localstack.GetEndpoint("main"))
    // API-only mode — no queue workers in this process
    .WithArgs("--mode", "api");

// Worker project — processes all queues, exposes only health checks (no API/UI).
// Runs the same Api project in worker mode so it shares handler code and module registrations.
builder.AddProject<Projects.Api>("worker")
    .WithHttpEndpoint()
    .WithHttpsEndpoint()
    .WithReplicas(3)
    .WaitFor(localstack)
    .WaitFor(redis)
    .WithReference(localstack.GetEndpoint("main"))
    .WithReference(redis)
    .WithEnvironment("AWS__ServiceURL", localstack.GetEndpoint("main"))
    // Worker mode — health checks only, all queue workers active
    .WithArgs("--mode", "worker");

// Run a single Vite frontend for all API replicas in distributed mode.
builder.AddViteApp("web", "../Web")
    .WithHttpsEndpoint(port: 5199, env: "PORT")
    .WithHttpsDeveloperCertificate()
    .WithExternalHttpEndpoints()
    .WithReference(api)
    // Provide an explicit API proxy target (not VITE_ prefixed so it stays server-side).
    .WithEnvironment("API_PROXY_TARGET", api.GetEndpoint("https"));

builder.Build().Run();
