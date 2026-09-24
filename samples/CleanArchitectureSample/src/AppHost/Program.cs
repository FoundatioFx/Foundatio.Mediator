var builder = DistributedApplication.CreateBuilder(args);

// SAMPLE_TOPOLOGY=single runs the API and every worker in one process. The default, "split", runs an
// enqueue-only API and one worker deployment per group. Same project either way; only --mode/--workers differ.
bool singleProcess = string.Equals(builder.Configuration["SAMPLE_TOPOLOGY"], "single", StringComparison.OrdinalIgnoreCase);

// Floci provides SQS + SNS for local development
var floci = builder.AddContainer("floci", "floci/floci", "2.1.0")
    .WithHttpEndpoint(targetPort: 4566, name: "main")
    .WithHttpHealthCheck("/_floci/health", endpointName: "main");

// Redis for shared persistence, distributed caching, job state, and the [QueueLock] lock
var redis = builder.AddRedis("redis");

IResourceBuilder<ProjectResource> AddNode(string name, params string[] args) =>
    builder.AddProject<Projects.Api>(name)
        .WithHttpEndpoint()
        .WithHttpsEndpoint()
        .WaitFor(floci)
        .WaitFor(redis)
        .WithReference(floci.GetEndpoint("main"))
        .WithReference(redis)
        .WithEnvironment("AWS__ServiceURL", floci.GetEndpoint("main"))
        .WithArgs(args);

IResourceBuilder<ProjectResource> api;
if (singleProcess)
{
    api = AddNode("api").WithExternalHttpEndpoints();
}
else
{
    api = AddNode("api", "--mode", "api").WithExternalHttpEndpoints().WithReplicas(2);
    AddNode("worker-exports", "--mode", "worker", "--workers", "exports").WithReplicas(2);
    AddNode("worker-imports", "--mode", "worker", "--workers", "imports");
    AddNode("worker-events", "--mode", "worker", "--workers", "events");
}

// One Vite dev server fronts every API replica.
builder.AddViteApp("web", "../Web")
    .WithHttpsEndpoint(port: 5199, env: "PORT")
    .WithHttpsDeveloperCertificate()
    .WithExternalHttpEndpoints()
    .WithReference(api)
    // Explicit proxy target (not VITE_ prefixed so it stays server-side).
    .WithEnvironment("API_PROXY_TARGET", api.GetEndpoint("https"));

builder.Build().Run();
