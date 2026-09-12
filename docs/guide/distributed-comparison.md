---
title: "Native Core Alternative"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 70
---

# Native Core Alternative to PR 149

This implementation keeps the handler experience while moving delivery into Foundatio's native messaging runtime. **The existing Mediator runtime, source generator, and core test projects match `main` (`3f76a7e`) exactly.** It uses **one Mediator integration package**, native AWS messaging, the Redis job store and lock provider, and the native test harness. There is no `IQueueClient`, `IPubSubClient`, compatibility context, or Mediator provider package.

The Foundatio messaging and job-store changes are included directly in [Foundatio PR #533](https://github.com/FoundatioFx/Foundatio/pull/533). The exact source dependency is pinned in `build/foundatio-core.json`; `build/setup-foundatio-core.ps1` reproduces it. Packaging the integration is disabled until its native core dependency is released.

## The application experience

```csharp
var foundatio = builder.Services.AddFoundatio();
foundatio.Messaging.UseAws();
foundatio.Jobs.UseRedis();
foundatio.Locking.UseRedis();

builder.Services.AddMediator()
    .AddDistributedQueues(options => options.Workers =
        WorkerSelection.Parse(builder.Configuration["Distributed:Workers"]))
    .AddDistributedNotifications(options => options.Include<OrderChanged>());
```

Handlers remain ordinary methods with convention discovery, scoped dependencies, middleware, and `Result` returns. `[Queue]`, groups, concurrency, retry schedules, progress, cancellation, `[QueueLock]`, enqueue receipts, and cascading events remain available. A handler needing progress takes **native `MessageProcessingContext`** directly.

The Clean Architecture sample demonstrates validation before enqueue, tracked exports, retrying webhooks, terminal failures, imports, bank resource locks, event feeds, worker selection, and queue administration. The UI exposes replay lineage as typed fields, rather than interpreting provider wire headers. The console sample shows a complete started-host workflow without Docker.

## Built through existing Mediator features

`[Queue]` attaches an ordinary `BeforeAsync` middleware through `[UseMiddleware]`. Caller-side routing short-circuits with acceptance. The worker provides native `MessageProcessingContext` through the existing `CallContext`, so the same middleware continues to the handler. No dispatcher marker or stage enum is added to core.

Validation uses normal `OrderBefore = [typeof(QueueMiddleware)]` and runs on both sides. Standard short-circuit lifecycle rules apply: `After` is skipped on acceptance and `Finally` runs. `ExecuteAsync` wraps the normal pipeline. `ScopedPerInvoke` keeps its existing scope ownership, including an additional invocation scope inside a worker scope.

The notification bridge observes `SubscribeAsync`, which works with both generated publish interceptors and runtime publication, including messages with no local handlers. Distribution filters and inbound echo suppression live in the extension. Explicit notification selections use typed subscriptions, keeping unrelated local types out of their buffers. Overlapping selections publish once and share the transport concurrency limit. Dynamic predicates and concrete value-type selections use the shared object subscription. Exact buffer drop counts are unavailable.

A successful `EnqueueAsync` returns the typed receipt through the existing `Result<T>` success API (`Status = Ok`). Invoking a queued handler directly still returns `Accepted`. Neither operation returns the eventual business result. Queued cascading tuples use reference-type or nullable event items; non-nullable value-type events should be published explicitly inside the handler.

## Ownership and boundaries

| Responsibility | PR 149 | Native alternative |
| --- | --- | --- |
| Discovery, middleware, generated invocation | Mediator with distributed stage/dispatcher changes | Unchanged Mediator core |
| Queue routing and interpreting `Result` | Distributed extension plus core dispatcher logic | Ordinary middleware in the distributed extension |
| Receiving, capacity, serialization, delivery leases, settlement | Mediator distributed runtime | Foundatio messaging runtime |
| AWS transport and batching | Mediator AWS package | Native Foundatio AWS transport |
| Progress, cancellation, execution history | Mediator state stores | Existing job stores and `JobState`; broker records cannot enter runtime claims |
| Redis resource ownership | Sample provider | Native Redis lock provider |
| Node resource lifecycle | Mediator AWS pub/sub client | Native managed node subscriptions |
| Dead-letter inspection and recovery | Mediator provider operations | Native message administration |
| Testing delivery and provider contracts | Mediator transport harness | Native Foundatio harness and conformance suites |

Default queued notification handlers receive independent copies with independent retry budgets. Explicitly sharing `QueueName` opts into one delivery and retry unit. Each attempt starts with a worker scope; handlers retain their normal lifetime semantics, including `ScopedPerInvoke`. Shared queues use Mediator's existing publication order without registry mutation. Every caller pipeline can run, while queue middleware sends only from the first matching registration; this is not atomic validation across handlers. Only registered message types may be deserialized.

Keep the application contracts explicit:

- **`InvokeAsync`** on a local handler waits for execution and can return its result.
- **`EnqueueAsync`** means broker acceptance, with a receipt for separately observed completion. It is not a transaction with business data or tracking persistence.
- **Distributed `PublishAsync`** is best effort. A bounded outbound buffer, disconnected nodes, and subscription recovery can lose notifications. Durable business side effects belong in queued handlers.

Queuing still changes serialization, authorization timing, failure handling, and idempotency requirements. Shared infrastructure does not make local invocation and distributed delivery interchangeable. Existing `InvokeAsync` support on a queued handler returns acceptance; examples prefer `EnqueueAsync` to make the boundary visible.

## One job store

Tracked queue work and ordinary background jobs share `IJobRuntimeStore`, `JobState`, `IJobMonitor`, and the existing in-memory/Redis implementations. Configure `Jobs.UseInMemory()` or `Jobs.UseRedis()` separately from messaging. The dashboard uses the normal query/count APIs, and cancellation is part of the returned job snapshot.

`JobExecutionOwner.Broker` excludes queued work from runtime claims, retry scheduling, and lease recovery. The message bus still owns delivery leases and settlement. Every delivery attempt receives a fresh state-update token, so a delayed progress report or completion cannot overwrite a newer attempt. Retained history can expire without preventing broker work from running.

## Implementation size

Counting C# lines including comments and blanks, excluding generated files:

| Mediator distributed surface | PR 149 | Native alternative |
| --- | ---: | ---: |
| Packages | 4 | 1 |
| C# files | 70 | 32 |
| C# lines | 8,442 | 3,098 |
| Existing Mediator core files changed | 11 | 0 |

That removes about 64% of the Mediator-owned distributed source. Queue tracking and ordinary jobs use the same Foundatio job stores, with provider and lifecycle coverage shared in its test harness. These capabilities are real shared-core work, not a free dependency substitution. This comparison does not count the existing #533 runtime as newly implemented code.

## Performance and validation

The [September 12 production quality report](https://github.com/FoundatioFx/Foundatio.Mediator/tree/codex/core-distributed-alternative/benchmarks/Foundatio.Mediator.Distributed.Benchmarks/comparison/production-pass-2026-09-12) compares the previous native version, this update, and PR #149 on system .NET 10.0.12 in Release.

| Workload | PR #149 jobs/s | Before jobs/s | After jobs/s | Allocated bytes/job, before → after |
| --- | ---: | ---: | ---: | ---: |
| In memory, concurrency 64 | 181,195 | 95,696 | 100,346 | 8,358 → 7,928 |
| In memory, tracked, concurrency 64 | 36,658 | 33,238 | 34,111 | 15,535 → 15,146 |
| In memory, concurrency 1 | 110,805 | 93,045 | 100,537 | 8,686 → 8,271 |
| In memory, concurrency 8 | 177,069 | 84,916 | 91,161 | 8,340 → 7,934 |
| SQS / LocalStack, concurrency 64 | 2,788 | 3,065 | 2,968 | 47,026 → 46,623 |
| In memory + Redis tracking, concurrency 64 | 10,619 | 7,757 | 7,600 | 45,852 → 45,450 |
| SQS / LocalStack + Redis tracking, concurrency 64 | 2,702 | 2,653 | 2,582 | 84,285 → 83,930 |

Untracked memory throughput improved **5–8%**, with **about 5% fewer allocated bytes**. The normal 1 ms receive policy improved about 7%. Longer memory-tracking runs were level; longer Redis tracking measured **4% lower throughput with 5% less CPU**. PR #149 remains leaner in memory; this change does not establish a Redis or SQS throughput gain.

The runtime starts asynchronous lease monitoring only when its first check is due. Expiry supervision remains active from admission. The new layer benchmark isolates transport, supervised receiving, native execution, and Mediator dispatch. Release builds also now preserve the configuration of native project references, and CI rejects unoptimized benchmark assemblies.

All **63 matrix trials (4.32M jobs)** and **30 longer/default trials (3.2M jobs)** completed without missing or duplicate deliveries. Three rotating trials per matrix cell use a 1,000-message warmup and a 256-character payload; timing includes broker drain and tracked completion. LocalStack is not production AWS capacity. The linked report retains raw data, fingerprints and excluded mixed-build trials.

A ten-minute recovery run accepted **69,354 jobs**, completed **69,314**, and cancelled the expected **40**. All **32** deliveries interrupted by a crash retried after replacement. Another worker restarted gracefully during arrivals. No pending/failed jobs or duplicate effects remained; the handler deliberately uses idempotent effects.

Validation: full Release builds, **2,235 Foundatio tests** (24 existing skips), **756 Mediator tests**, **23 browser scenarios** against separate Release API/worker processes, frontend checks/build, console walkthrough, Quickstart and docs links. Foundatio retains its existing AppHost ASPIRE010 warning; Mediator builds without warnings.

AWS node queues use heartbeat and cleanup rather than native TTL. Dead-letter fallback and replay send before deleting, so uncertain outcomes can duplicate work. History is operational tracking rather than permanent deduplication or fencing of external writes. A real AWS staging soak with deployment permissions and production-shaped handlers remains release work.

## Recommendation

I prefer the native-core direction if #533 is the foundation the project intends to ship. It gives one delivery runtime and one set of providers, and makes core improvements available beyond Mediator. The sample remains small and ordinary, with infrastructure configured separately from application handlers.

I would not choose it on a claim of universally faster execution: PR 149 is leaner in the in-memory measurements. The native approach also couples the initial release to new core APIs that need review. If the priority is an independent Mediator release or minimum in-process queue overhead, PR 149 has a practical advantage.

Keep the convenience of convention-based handlers through the existing extension points, with zero distributed changes to core. Make enqueue acceptance, local invocation, and best-effort broadcast visibly different operations. That preserves the developer experience without hiding the distributed contract.
