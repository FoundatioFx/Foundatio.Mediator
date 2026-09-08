---
title: "Native Core Alternative"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 70
---

# Native Core Alternative to PR 149

This implementation keeps the handler experience while moving delivery into Foundatio's native messaging runtime. It uses **one Mediator integration package**, native AWS messaging, the Redis job store and lock provider, and the native test harness. There is no `IQueueClient`, `IPubSubClient`, compatibility context, or Mediator provider package.

The core changes are included directly in [Foundatio PR #533](https://github.com/FoundatioFx/Foundatio/pull/533). The exact source dependency is pinned in `build/foundatio-core.json`; `build/setup-foundatio-core.ps1` reproduces it. Packaging the integration is disabled until its native core dependency is released.

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

## Ownership and boundaries

| Responsibility | PR 149 | Native alternative |
| --- | --- | --- |
| Discovery, middleware stages, generated invocation | Mediator | Mediator |
| Queue routing and interpreting `Result` | Mediator | Mediator |
| Receiving, capacity, serialization, delivery leases, settlement | Mediator distributed runtime | Foundatio messaging runtime |
| AWS transport and batching | Mediator AWS package | Native Foundatio AWS transport |
| Progress, cancellation, execution history | Mediator state stores | Existing job stores and `JobState`; broker records cannot enter runtime claims |
| Redis resource ownership | Sample provider | Native Redis lock provider |
| Node resource lifecycle | Mediator AWS pub/sub client | Native managed node subscriptions |
| Dead-letter inspection and recovery | Mediator provider operations | Native message administration |
| Testing delivery and provider contracts | Mediator transport harness | Native Foundatio harness and conformance suites |

Default queued notification handlers receive independent copies with independent retry budgets. Explicitly sharing `QueueName` opts into one delivery and retry unit. Each attempt uses one fresh DI scope. Only registered message types may be deserialized.

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
| C# lines | 8,442 | 3,044 |

That removes about 64% of the Mediator-owned distributed source. The core extension adds **2,055 net lines in Foundatio's `src` tree**, including testing support. Queue tracking and ordinary jobs use the same job stores. These capabilities are real shared-core work, not a free dependency substitution. This comparison does not count the existing #533 runtime as newly implemented code.

## Measured performance

Measurements below use the unified job store at core revision `823972fc`, recorded in the raw results. Subsequent cleanup removed unused store types and registrations without changing the measured execution path. Three alternating repetitions on the same Linux development machine and .NET 10.0.11, Release, concurrency 64, a 256-character payload, and 1,000-message warmup. Each in-memory run processes 10,000 messages; each SQS run processes 2,000. Values below are medians. Every run verified all unique messages completed with zero duplicates.

| Scenario | PR 149 messages/s | Native messages/s | Allocated bytes/message: PR 149 / native |
| --- | ---: | ---: | ---: |
| In memory | 168,758 | 66,513 | 6,169 / 11,228 |
| In memory, tracked | 84,033 | 35,640 | 11,487 / 19,157 |
| SQS / LocalStack | 3,050 | 2,810 | 39,855 / 48,873 |

Median per-run p99 latency, in milliseconds (PR 149 / native):

| Scenario | Enqueue acceptance | Handler entry, including queue wait |
| --- | ---: | ---: |
| In memory | 0.33 / 2.89 | 26.00 / 97.77 |
| In memory, tracked | 1.62 / 5.13 | 3.38 / 160.44 |
| SQS / LocalStack | 19.65 / 31.41 | 317.62 / 213.87 |

The native implementation has higher dispatch and allocation overhead in memory. Enqueue tail latency is also higher in these runs. The LocalStack run puts broker throughput in the same general range, with PR 149 ahead at the median in this run set. Three short runs on a shared machine and an emulator do not establish production AWS capacity or a general speed advantage. Tracking measurements use in-memory stores to isolate runtime overhead; live Redis correctness is verified separately. These results do not measure payload-heavy processing, crash recovery throughput, or cross-region latency.

Timing includes sending and draining the broker. Latency samples end at handler entry, and SQS drain uses approximate statistics. Host startup, warmup, and shutdown are excluded. Generated local Mediator dispatch is unchanged from PR 149.

The [comparison runner and raw measurements](https://github.com/FoundatioFx/Foundatio.Mediator/tree/codex/core-distributed-alternative/benchmarks/Foundatio.Mediator.Distributed.Benchmarks) include the pinned PR 149 baseline, standalone programs, measurement settings, and all 18 results. Run `compare.ps1`, `compare.ps1 -Tracking`, and `compare.ps1 -Aws -Messages 2000` to repeat them.

## Validation

- Foundatio: full solution build; **2,212 tests passed**, 24 documented capability/platform/benchmark skips, with isolated Redis and LocalStack configured. Superseded store fixtures were removed after their lifecycle coverage moved into the shared job-store conformance suite.
- Mediator: the default pinned-source build has zero warnings; **746 tests passed**, including native integration and a custom serializer across queue and notification boundaries.
- Sample: frontend type check and production build passed; **23 Playwright scenarios passed against separate API and worker processes**. The console workflow completes validation, enqueue, progress, and native state observation.
- Focused failure coverage includes conservative/manual lease renewal, graceful drain, failed acknowledgment remaining nonterminal, expired history not blocking work, stale-attempt fencing, Redis lock ownership, unknown wire types, independent node copies, and SQS replay beyond the first receive batch.

AWS node queues use explicit heartbeat and cleanup, not native TTL leases. Dead-letter fallback and replay send before deleting, so uncertain outcomes can duplicate work. Execution history is retained operational state, not permanent deduplication or fencing of external business writes. These limits are part of the contract.

## Recommendation

I prefer the native-core direction if #533 is the foundation the project intends to ship. It gives one delivery runtime and one set of providers, and makes core improvements available beyond Mediator. The sample remains small and ordinary, with infrastructure configured separately from application handlers.

I would not choose it on a claim of universally faster execution: PR 149 is leaner in the in-memory measurements. The native approach also couples the initial release to new core APIs that need review. If the priority is an independent Mediator release or minimum in-process queue overhead, PR 149 has a practical advantage.

Keep the convenience of convention-based handlers while making enqueue acceptance, local invocation, and best-effort broadcast visibly different operations. That preserves the developer experience without hiding the distributed contract.
