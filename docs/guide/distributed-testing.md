---
title: "Testing Distributed Handlers"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 60
---

# Testing Distributed Handlers

Use the native `Foundatio.Testing` harness. Start a real host so infrastructure and consumers are ready, then wait for idle to include settlement and the final execution state write.

```csharp
var builder = Host.CreateApplicationBuilder();
builder.Services.AddFoundatio().Messaging
    .UseTestHarness()
    .UseInMemoryExecutionTracking();
builder.Services.AddMediator(options => options.AddAssembly<ExportHandler>())
    .AddDistributedQueues();

using var host = builder.Build();
await host.StartAsync(ct);
try
{
    var mediator = host.Services.GetRequiredService<IMediator>();
    var harness = host.Services.GetRequiredService<MessagingTestHarness>();
    var result = await mediator.EnqueueAsync(new ExportReport("monthly"), ct);
    await harness.WaitForIdleAsync(cancellationToken: ct);
    Assert.True(result.IsSuccess);
    Assert.Single(harness.Handled<ExportReport>());
}
finally
{
    await host.StopAsync(ct);
}
```

Imports are `Foundatio`, `Foundatio.Messaging`, `Foundatio.Messaging.Testing`, `Foundatio.Mediator`, and `Foundatio.Mediator.Distributed`, plus normal hosting and DI namespaces.

The Mediator integration suite verifies enqueue validation, receipts, retry results, terminal failures, scopes, headers, fanout, shared queues, manual settlement, cancellation, replay, wire-type allowlisting, and node broadcasts. Native delivery lease, shutdown, storage fencing, and provider contracts are tested in the pinned core source.

The Clean Architecture sample also has Playwright workflows for progress, cancellation, dead letters, replay, locks, and navigation against real Redis and LocalStack. Set `SAMPLE_BASE_URL` to the running local frontend, then run `npm run test:e2e` in its Web project.
