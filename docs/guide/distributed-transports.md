---
title: "Transport Providers"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 40
---

# Native Foundatio Providers

The integration depends on `Foundatio.Mediator.Distributed` and native Foundatio libraries. It has no AWS, Redis, testing, or custom transport compatibility layer.

## Source setup

This branch uses unreleased core additions on top of Foundatio PR #533. The exact core revision is pinned in `build/foundatio-core.json`.

```powershell
./build/setup-foundatio-core.ps1
dotnet build Foundatio.Mediator.slnx
dotnet test --solution Foundatio.Mediator.slnx --no-build
```

The script checks out the pinned core source into `.dependencies/Foundatio`. For active core development, pass `-p:FoundatioCorePath=/absolute/path/to/Foundatio` to the build. There is no placeholder NuGet version; publication waits until the native APIs have a released dependency.

## In memory

```csharp
var foundatio = builder.Services.AddFoundatio();
foundatio.Messaging.UseInMemory();
foundatio.Jobs.UseInMemory();
builder.Services.AddMediator().AddDistributedQueues();
```

This configuration is process-local and suitable for the console sample. Use a shared broker and store when API and workers run separately.

## AWS and Redis

```csharp
var foundatio = builder.Services.AddFoundatio();
foundatio.Messaging.UseAws();
foundatio.Jobs.UseRedis();
foundatio.Locking.UseRedis();

builder.Services.AddMediator()
    .ConfigureDistributed(options => options.ResourcePrefix = "orders-production")
    .AddDistributedQueues(options => options.Workers =
        WorkerSelection.Parse(builder.Configuration["Distributed:Workers"]))
    .AddDistributedNotifications(options => options.Include<OrderChanged>());
```

Register a shared `IConnectionMultiplexer` for Redis, as the sample does for its distributed cache. Configure AWS credentials through the SDK credential chain. For local development only, set the service endpoint to LocalStack and use its dummy credentials.

SQS supplies competing consumers; SNS supplies node broadcasts. Foundatio batches concurrent sends and acknowledgments. The application configures native serialization once through Foundatio's serializer registration; the integration uses that same serializer for queued messages and notifications.

## Provisioning {#provisioning}

Native `TopologyMode.Ensure` creates destinations; `Validate` checks that they exist; `None` assumes externally provisioned infrastructure. Configure these on the Foundatio messaging builder. Mediator declares its discovered queues and topics before accepting work.

Managed AWS node subscriptions intentionally create temporary tagged SQS queues and SNS subscriptions, even when durable topology is externally managed. They require queue creation/deletion, queue tagging/listing, topic subscription/unsubscription, and normal messaging permissions. Live nodes heartbeat; clean shutdown removes resources; new nodes reap stale subscriptions. These are managed resources, not native SQS TTL leases. A paused node that exceeds the stale window may lose its subscription.

Queue retry and dead-letter decisions belong to Foundatio. Do not add an independent SQS redrive policy for those queues. Nontransactional fallback dead-letter moves and replay remain at least once.

## Native transport extension points

Custom providers implement Foundatio's `IMessageTransport` and relevant capability interfaces. Mediator contains no provider-specific dispatch path. Provider conformance tests live with Foundatio core.
