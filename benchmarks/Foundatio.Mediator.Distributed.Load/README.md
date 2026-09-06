# Distributed worker load probe

Run from the repository root after a Release build:

```powershell
dotnet run --project benchmarks/Foundatio.Mediator.Distributed.Load -c Release --no-build -- 20000
# Optional existing local Redis instance:
dotnet run --project benchmarks/Foundatio.Mediator.Distributed.Load -c Release --no-build -- 20000 localhost:6379
# Separately measure Redis command round trips (profiling adds overhead):
dotnet run --project benchmarks/Foundatio.Mediator.Distributed.Load -c Release --no-build -- 5000 localhost:6379 --profile
```

The probe uses one producer, a no-op Result handler, an in-memory queue, concurrency 8, and prefetch 32. It measures acceptance through worker completion, including validation, serialization, state creation, dispatch, acknowledgment, and final state updates. Each run warms up first. It asserts every message completes and outstanding worker deliveries never exceed 8. Redis keys have a unique prefix and five-minute retention. Supply a disposable development Redis instance.

JSON output includes messages/second, p50/p95 end-to-end latency, process-wide allocated bytes/message, transport calls, and maximum outstanding deliveries. Allocations include the harness and runtime background work. `RedisOperations` is the multiplexer operation count, which can include background commands; it is not a network-round-trip count. Optional profiling reports completed commands and their mean send-to-response duration, including server and client response processing. Pipelining can overlap those durations.

## Measurement on the PR 149 hardening candidate

Ubuntu 26.04.1, AMD Ryzen AI 9 HX 470, .NET SDK 10.0.111/runtime 10.0.11, Redis 7 in a local Docker container. These are single-machine observations, not production capacity guarantees. Builds, tests, and other benchmarks were stopped during each measurement; OS scheduling and other machine activity still introduce variation. The no-op handler intentionally exposes infrastructure overhead. Add representative payloads, handler work, and a real queue transport for deployment sizing.

20,000 messages per configuration, without Redis profiling:

| Job state | Messages/s | p50 ms | p95 ms | Allocated B/message | Send / receive / complete calls | Max outstanding |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| Untracked | 163,622 | 0.015 | 0.029 | 6,096 | 20,000 / 18,955 / 20,000 | 8 |
| In-memory | 47,630 | 0.092 | 4.373 | 10,954 | 20,000 / 15,278 / 20,000 | 8 |
| Redis | 3,675 | 1.134 | 2.074 | 24,089 | 20,000 / 20,001 / 20,000 | 6 |

These short jobs required no lease renewals. Fake-time reliability tests cover long-running handlers, renewals, and stalled providers. Receive calls include an outstanding idle receive at the end where applicable. Lower latency with Redis than the memory-tracked p95 reflects producer pacing by Redis state creation, not a faster state store.

A separate profiled 5,000-job Redis run recorded 25,000 commands and a 0.209 ms mean command round trip. The unprofiled 20,000-job run recorded 100,003 multiplexer operations. Combining counter increment and expiry in one Lua operation reduced the earlier 5,000-job count from 45,000 to approximately 25,000 operations. Latency varied between runs, so this establishes command reduction rather than a claimed throughput speedup.

## In-process regression check

Compared the rebased baseline `288aa1a` with the hardening candidate using BenchmarkDotNet 0.15.8, one launch, three warmups, nine iterations, and both processes pinned to Linux CPU 0:

```bash
taskset -c 0 dotnet run --project benchmarks/Foundatio.Mediator.Benchmarks -c Release --no-build -- foundatio --filter '*FoundatioBenchmarks.Query' '*FoundatioBenchmarks.Publish' --launchCount 1 --warmupCount 3 --iterationCount 9
```

For checkouts under `/tmp`, set `TMPDIR` to a separate temporary directory so BenchmarkDotNet does not reject the benchmark assembly as a temporary build artifact.

| Method | Baseline mean | Candidate mean | Baseline 99.9% error | Candidate 99.9% error | Allocated, both |
| --- | ---: | ---: | ---: | ---: | ---: |
| Query | 21.10 ns | 21.38 ns | ±0.981 ns | ±0.535 ns | 48 B |
| Publish | 22.09 ns | 23.86 ns | ±1.802 ns | ±5.774 ns | 0 B |

Intervals overlap; no core dispatch regression was established. Unpinned runs varied substantially, so they are not used to estimate a change. The empty command benchmark is indistinguishable from measurement overhead and cannot support a useful speed claim. All generated benchmark source is identical after excluding version attributes/comments and interception source locations. Existing generator snapshots remain unchanged.

## Assessment of existing Foundatio primitives

The bounded comparison used the installed Foundatio 13.0.4 API contracts:

| Primitive | Fit and decision |
| --- | --- |
| `IQueue<T>` | Supports payloads, leases, retry policy, and metadata. It dequeues one entry; enqueue/settlement APIs lack cancellation tokens, abandonment lacks a per-call delay, and dead-letter retrieval exposes payloads rather than replayable receipts. Adapting it would still require the mediator transport contract and careful retry ownership. Keep `IQueueClient`; a separate adapter can target compatible providers. |
| `IMessagePublisher` / `IMessageSubscriber` | Typed events and cancellation-scoped subscriptions can fit behind an adapter. They do not remove the explicit wire topic or SNS/SQS per-node provisioning requirements. Retain `IPubSubClient` and its transport lifecycle. |
| `ILockProvider` | Acquisition and ownership IDs are a useful adapter target. Renewal/release need bounded operation wrappers and explicit lost-ownership behavior. Retain the small `IQueueLockProvider` extension point; do not add a mandatory dependency for every queue consumer. |

A wholesale transport replacement does not demonstrate simpler equivalent semantics here. Handler-resolution allocation changes remain deferred; measured Redis command reduction and bounded leases take priority. Transactional outbox/inbox support remains an application integration concern, documented in the queue guide.
