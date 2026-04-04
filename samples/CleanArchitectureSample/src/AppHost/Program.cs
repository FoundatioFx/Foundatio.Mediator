var builder = DistributedApplication.CreateBuilder(args);

// LocalStack provides SQS + SNS for local development
var localstack = builder.AddContainer("localstack", "localstack/localstack", "latest")
    .WithHttpEndpoint(targetPort: 4566, name: "main")
    .WithHttpHealthCheck("/_localstack/health", endpointName: "main")
    .WithEnvironment("SERVICES", "sqs,sns");

// Redis for shared persistence and distributed caching
var redis = builder.AddRedis("redis");

// The API project with 3 replicas to demonstrate distributed pub/sub fan-out
var api = builder.AddProject<Projects.Api>("api")
    // Expose dynamic API endpoints externally so dashboard links and references resolve correctly.
    .WithExternalHttpEndpoints()
    .WithReplicas(3)
    .WaitFor(localstack)
    .WaitFor(redis)
    .WithReference(localstack.GetEndpoint("main"))
    .WithReference(redis)
    .WithEnvironment("AWS__ServiceURL", localstack.GetEndpoint("main"))
    // Disable per-replica SpaProxy startup; AppHost owns a single frontend process.
    .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", string.Empty);

// Run a single Vite frontend for all API replicas in distributed mode.
builder.AddViteApp("web", "../Web")
    .WithHttpsEndpoint(port: 5199, env: "PORT")
    .WithHttpsDeveloperCertificate()
    .WithExternalHttpEndpoints()
    .WithReference(api)
    // Provide an explicit API proxy target (not VITE_ prefixed so it stays server-side).
    .WithEnvironment("API_PROXY_TARGET", api.GetEndpoint("https"));

builder.Build().Run();
