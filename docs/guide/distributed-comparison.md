---
title: "Native Core Alternative"
nav:
    section: "Distributed"
    sectionOrder: 25
    order: 70
---

# Native Core Alternative to PR 149

This implementation keeps the handler experience while moving delivery into Foundatio's native messaging runtime. **The existing Mediator runtime, source generator, and core test projects match `main` (`a148013`) exactly.** It uses **one Mediator integration package**, native AWS messaging, the Redis job store and lock provider, and the native test harness. There is no `IQueueClient`, `IPubSubClient`, compatibility context, or Mediator provider package.

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

## Measured performance

The latest September 8 follow-up keeps Mediator core at `a148013`. Its baseline is Mediator `ff9c155` / Foundatio `54006ac7`; PR #149 is measured at `89bd6d1`. The final dependency revision is recorded in `build/foundatio-core.json`.

Median jobs/second on the same shared Linux development host, Release, system .NET 10.0.11:

| Scenario | PR #149 jobs/s | Before jobs/s | After jobs/s | Allocated bytes/job, before → after |
| --- | ---: | ---: | ---: | ---: |
| In memory, concurrency 64 | 185,107 | 100,073 | 97,236 | 9,847 → 8,353 |
| In memory, tracked, concurrency 64 | 37,120 | 31,049 | 30,584 | 17,049 → 15,533 |
| In memory, concurrency 1 | 102,281 | 81,347 | 87,539 | 10,195 → 8,685 |
| In memory, concurrency 8 | 180,014 | 80,149 | 84,685 | 9,852 → 8,333 |
| SQS / LocalStack, concurrency 64 | 2,883 | 2,809 | 2,846 | 48,011 → 47,013 |
| In memory + Redis tracking, concurrency 64 | 8,428 | 5,898 | 5,409 | 47,360 → 45,831 |
| SQS / LocalStack + Redis tracking, concurrency 64 | 2,414 | 2,498 | 2,445 | 85,479 → 84,388 |

Untracked in-memory jobs allocate **15% less**, tracked memory **9% less**, and Redis-tracked jobs **3% less**. Concurrency 1/8 throughput improves 8%/6%; high-concurrency memory and LocalStack remain near the baseline. Redis's short runs varied; five longer alternating pairs measured 6,380 → 6,503 jobs/s, with 13% less process CPU. That does not establish a consistent Redis throughput gain. PR #149 still has lower in-memory overhead.

The [complete report and raw measurements](https://github.com/FoundatioFx/Foundatio.Mediator/tree/codex/core-distributed-alternative/benchmarks/Foundatio.Mediator.Distributed.Benchmarks/comparison/optimization-pass2-2026-09-08) contain all 63 successful matrix runs (4.32M measured jobs), p99 latency, longer Redis checks, source/binary fingerprints and recovery evidence. Three rotating repetitions per cell use a 1,000-message warmup and 256-character payload. Timing includes broker drain and tracked completion; LocalStack is not production AWS capacity. One additional PR #149 trial and one build hit the known CLR abort and passed on same-runtime retries; the evidence retains both failures.

A two-minute arrival test accepted **46,504 jobs**, completed **46,464**, and cancelled the expected **40**. A forced process crash interrupted 32 handlers; all 32 retried after restart. A second worker stopped and restarted gracefully during arrivals. Nothing remained pending or failed. The harness uses idempotent effects and does not imply exactly-once execution.

Paced and mixed notification checks each delivered all 10,000 selected cluster events without duplicates. The [preceding full notification study](https://github.com/FoundatioFx/Foundatio.Mediator/tree/codex/core-distributed-alternative/benchmarks/Foundatio.Mediator.Distributed.Benchmarks/comparison/optimization-2026-09-08) retains the buffer-overload results and the 193 → 56 ms improvement for one million local-only events with the bridge enabled.

Use the [queue benchmark runner](https://github.com/FoundatioFx/Foundatio.Mediator/tree/codex/core-distributed-alternative/benchmarks/Foundatio.Mediator.Distributed.Benchmarks) to repeat the comparisons. The sibling notification benchmark reproduces paced, burst and mixed traffic. Earlier studies remain in the comparison directory.

## Validation

- Foundatio: full solution build; **2,229 tests passed**, 24 documented capability/platform/benchmark skips, with isolated Redis and LocalStack configured. Coverage includes older Redis records, bounded cleanup, immutable header snapshots, single-message failure outcomes, cancellation expiry, overlapping settlement and stale receipts. The existing AppHost ASPIRE010 warning remains.
- Mediator: the pinned-source build has zero warnings; **756 tests passed**, including native integration and a custom serializer across queue and notification boundaries.
- Sample: frontend type check and production build passed; **23 Playwright scenarios passed against separate API and worker processes**. The console workflow completes validation, enqueue, progress, and native state observation.
- Focused failure coverage includes conservative/manual lease renewal, graceful drain, failed acknowledgment remaining nonterminal, expired history not blocking work, stale-attempt fencing, Redis lock ownership, unknown wire types, independent node copies, and SQS replay beyond the first receive batch.

AWS node queues use explicit heartbeat and cleanup, not native TTL leases. Dead-letter fallback and replay send before deleting, so uncertain outcomes can duplicate work. Execution history is retained operational state, not permanent deduplication or fencing of external business writes. These limits are part of the contract.

## Recommendation

I prefer the native-core direction if #533 is the foundation the project intends to ship. It gives one delivery runtime and one set of providers, and makes core improvements available beyond Mediator. The sample remains small and ordinary, with infrastructure configured separately from application handlers.

I would not choose it on a claim of universally faster execution: PR 149 is leaner in the in-memory measurements. The native approach also couples the initial release to new core APIs that need review. If the priority is an independent Mediator release or minimum in-process queue overhead, PR 149 has a practical advantage.

Keep the convenience of convention-based handlers through the existing extension points, with zero distributed changes to core. Make enqueue acceptance, local invocation, and best-effort broadcast visibly different operations. That preserves the developer experience without hiding the distributed contract.
