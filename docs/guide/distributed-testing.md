---
title: "Testing Distributed Handlers"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 60
---

# Testing Distributed Handlers

Queued handlers are plain classes, so unit tests call them directly. Integration tests want two more things: to assert what a handler enqueued, and to run workers to completion deterministically. The `Foundatio.Mediator.Distributed.Testing` package provides both.

```bash
dotnet add package Foundatio.Mediator.Distributed.Testing
```

## Asserting What Was Enqueued

`RecordingQueueClient` wraps a queue client (by default an in-memory one) and records every send:

```csharp
var queue = new RecordingQueueClient();

var services = new ServiceCollection();
services.AddSingleton<IQueueClient>(queue);
services.AddMediator().AddDistributedQueues();          // workers run in the test process
// or, to assert only what was enqueued:
// .AddDistributedQueues(o => o.Workers = WorkerSelection.None)
```

```csharp
await mediator.InvokeAsync(new CreateOrder("ORD-1"), ct);

var sent = Assert.Single(queue.Sent<SendConfirmation>());
Assert.Equal("ORD-1", sent.OrderId);
Assert.True(queue.WasSent<SendConfirmation>());
Assert.Equal("SendConfirmation", queue.SentMessages[0].QueueName);
```

`Sent<T>()` deserializes the recorded bodies whose type header names `T`. `SentMessages` exposes queue name, headers (including the job id), and timestamp for every send.

## Running Workers to Completion

Start the hosted services as usual, then wait for the queues to drain instead of sleeping:

```csharp
await mediator.InvokeAsync(new ImportFile("a.csv"), ct);
await queue.DrainAsync(TimeSpan.FromSeconds(10), ct);   // no pending or in-flight messages remain
Assert.Equal(1, repository.ImportedFiles.Count);
```

`DrainAsync` throws with a per-queue summary if messages are still pending when the timeout passes. Dead-letter queues are not drained, so a test can assert on them afterwards.

## Two Nodes in One Process

`InMemoryTransport` is one queue client, pub/sub client, and lock provider that several service providers share. Build two collections against it and you have two nodes:

```csharp
await using var transport = new InMemoryTransport();

ServiceProvider BuildNode(string hostId) => new ServiceCollection()
    .AddLogging()
    .AddInMemoryDistributedTransport(transport)
    .AddMediator(b => b.AddAssembly<OrderCreatedHandler>())
    .AddDistributedQueues()
    .AddDistributedNotifications(o => o.HostId = hostId)
    .Services.BuildServiceProvider();

await using var nodeA = BuildNode("a");
await using var nodeB = BuildNode("b");
// start IHostedService instances on both, publish on A, assert B's handler ran once and
// transport.Queues.SentMessages.Count == 1
```

The in-memory queue models real lease semantics: a received message is invisible until completed, abandoned, or its visibility timeout lapses, and delayed abandons are scheduled on the supplied `TimeProvider`. Pass a `FakeTimeProvider` to the transport and to the service collection to drive retry delays and renewals without waiting.

## Testing the Worker Itself

The library's own suites are a useful reference: `tests/Foundatio.Mediator.Distributed.Tests` covers dispatch to several handlers, interface-typed handlers, one execution per inbound notification across two nodes, poison messages, renewal after a failed renew, drain on stop, locks, header providers, and the administration handlers, all in memory. The AWS and Redis suites run against LocalStack and Redis containers and skip themselves when Docker is unavailable.
