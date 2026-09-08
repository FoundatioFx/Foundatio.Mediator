---
title: "Going Distributed"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 10
---

# Going Distributed

This alternative uses Foundatio's native message bus for delivery and Mediator's generated handlers for application code. It builds on Foundatio PR #533; follow the [source setup](./distributed-transports#source-setup) before building this branch.

Start with the [console sample](https://github.com/FoundatioFx/Foundatio.Mediator/tree/codex/core-distributed-alternative/samples/DistributedConsoleSample), then explore the Clean Architecture sample's **Try it** and **Queues** pages.

## Choose the contract explicitly

| Operation | What completion means | Intended use |
| --- | --- | --- |
| `InvokeAsync` on a local handler | The handler finished | Queries and synchronous commands |
| `EnqueueAsync` on a `[Queue]` handler | The broker accepted work; the receipt identifies optional tracking | Work that may run later or elsewhere |
| `PublishAsync` with distributed notifications | Local dispatch finished; remote publication was buffered | Cache invalidation and live UI signals |

A queue is an at-least-once boundary. Handlers must tolerate duplicates, messages must serialize, and acceptance is separate from successful processing. Queue handlers retain ordinary method parameters, dependency injection, middleware, cascading events, and `Result` handling.

Distributed notifications are best effort. Disconnected nodes and a full outbound buffer can lose events. Use independent queued handlers for business side effects that need retries.

## Register infrastructure once

```csharp
var foundatio = builder.Services.AddFoundatio();
foundatio.Messaging.UseInMemory();
foundatio.Jobs.UseInMemory();
builder.Services.AddMediator().AddDistributedQueues();
```

For multiple processes, configure native AWS messaging and shared Redis tracking and locking. There are no Mediator transport packages or compatibility interfaces. Foundatio owns broker capacity, delivery leases, retry settlement, execution history, and provider administration. Mediator owns discovery, routing, scoped invocation, and interpretation of handler results.

Tracking records broker-driven execution; it does not schedule the same message through a second job runtime.

## Scale the same application

`WorkerSelection.Parse(configuration["Distributed:Workers"])` accepts `all`, `none`, named queues or groups, and exclusions such as `!imports`. Every host knows the complete topology, even when it runs no workers.

Default queued notification handlers each get an independent queue and retry budget. Explicitly naming the same `QueueName` groups handlers into one delivery and retry unit. A later handler failure can rerun earlier handlers in that group.
