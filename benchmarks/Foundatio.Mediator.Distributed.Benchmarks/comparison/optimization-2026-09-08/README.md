# Performance validation — September 8, 2026

Queue medians compare PR #149 (`89bd6d1`), the previous native implementation (Mediator `1623285`, Foundatio `9288e40`), and this optimization. Mediator core matches main `a148013`. Runtime source fingerprints and measured binary hashes are in `manifest.json` and the source hash files.

All runs used the normal Ubuntu `/usr/bin/dotnet`, .NET 10.0.11, Release, on the same shared Linux development host. Redis 7 and LocalStack 3.8.1 ran locally. These are end-to-end no-op handler measurements, not core dispatch microbenchmarks or production AWS capacity estimates.

## Queue throughput

| Scenario | PR #149 jobs/s | Before jobs/s | After jobs/s | Before → after allocated bytes/job |
| --- | ---: | ---: | ---: | ---: |
| In memory, concurrency 64 | 188,648 | 91,952 | 99,679 | 11,279 → 9,843 |
| In memory, tracked, concurrency 64 | 37,457 | 31,672 | 30,949 | 19,205 → 17,045 |
| In memory, concurrency 1 | 100,960 | 83,481 | 84,268 | 11,635 → 10,196 |
| In memory, concurrency 8 | 181,919 | 77,349 | 80,971 | 11,296 → 9,859 |
| SQS / LocalStack | 2,602 | 2,779 | 2,894 | 48,959 → 48,003 |
| In memory + Redis tracking | 8,688 | 1,695 | 6,153 | 49,721 → 47,366 |
| SQS / LocalStack + Redis tracking | 2,634 | 1,380 | 2,597 | 90,257 → 85,535 |

Redis tracking throughput improves **3.63×** with an in-memory transport and **1.88×** with LocalStack. Redis allocations fall about 5%; ordinary in-memory queue allocations fall 13%. PR #149 remains faster and leaner in memory; LocalStack results are broadly comparable.

In-memory tracking initially measured 15% slower. Five additional alternating pairs put throughput essentially level; pooling all eight native runs gives 31,672 before versus 30,949 after (2.3% lower), with 11% fewer allocated bytes. The raw files retain both batches. This scenario does not establish a throughput gain.

## Queue latency

Medians of per-run p99 latency, milliseconds. Handler entry includes queue waiting but excludes handler execution and final settlement.

| Scenario | Acceptance: before → after | Handler entry: before → after |
| --- | ---: | ---: |
| In memory, concurrency 64 | 2.77 → 2.33 | 1315.19 → 1230.39 |
| In memory, tracked, concurrency 64 | 4.86 → 4.79 | 927.10 → 1020.38 |
| In memory, concurrency 1 | 0.02 → 0.01 | 229.23 → 277.76 |
| In memory, concurrency 8 | 0.04 → 0.04 | 798.45 → 773.82 |
| SQS / LocalStack | 32.53 → 29.71 | 1155.96 → 1159.45 |
| In memory + Redis tracking | 46.99 → 11.46 | 1655.17 → 699.35 |
| SQS / LocalStack + Redis tracking | 51.16 → 28.12 | 2755.73 → 1300.36 |

## Notifications

Three alternating repetitions of each case, using two hosts and an in-memory transport. Numbers are medians; remote counts come from the handler itself.

| Scenario | Publisher time: before → after | Remote unique: before → after | Allocated bytes/publish: before → after |
| --- | ---: | ---: | ---: |
| 1M local events, bridge off | 28 → 28 ms | — | 24 → 24 |
| 1M local events, bridge on | 193 → 56 ms | — | 87 → 88 |
| 10k paced cluster events | 217 → 194 ms | 10,000 → 10,000 | 13900 → 12481 |
| 10k burst, capacity 1k | 22 → 22 ms | 1,001 → 1,001 | 256 → 259 |
| 10k cluster + 1M local, capacity 1k | 198 → 125 ms | 8,216 → 8,870 | 213 → 169 |
| 10k cluster + 1M local, capacity 10k | 269 → 124 ms | 8,482 → 10,000 | 216 → 170 |

Typed subscriptions keep unrelated local events out of explicitly selected cluster-event buffers. The deterministic regression test blocks the transport, publishes 10,000 local-only events, and verifies that all selected events remain buffered; the previous implementation fails that test. Overlapping selections publish once and share a concurrency limit.

Best-effort broadcasts still drop cluster events when their own buffers fill. A capacity of 10,000 delivered every mixed-workload cluster event in all three optimized runs; capacity 1,000 did not. Local-only publication is faster with typed subscriptions, with roughly unchanged allocations (88 versus 87 bytes per call with the bridge enabled). Burst allocation measurements exclude work that occurs after publication finishes. Dynamic predicates and concrete value-type selections retain the shared object subscription.

## Method and evidence

- Queue workloads use a 256-character payload and 1,000-message warmup. Counts per run: 200,000 untracked at concurrency 64; 100,000 at concurrency 1/8; 50,000 with memory tracking; 10,000 for Redis and/or LocalStack. Other scenarios use concurrency 64.
- Timing includes enqueue and broker drain; tracked runs also wait for the retained completed count. Startup, warmup, and shutdown are excluded. SQS drain relies on approximate statistics.
- `matrix-results.json`: three rotations of all seven workloads and three implementations. Its Redis optimization predates the final snapshot-array allocation reduction; those six optimized Redis runs are superseded by `final-redis-results.json`.
- `tracked-validation-results.json`: five more before/after pairs. The table pools these with the first three memory-tracking pairs; PR #149 has three runs.
- `final-redis-results.json`: three rotations of both Redis workloads and all three implementations after the final Redis change.
- **91 successful queue runs processed 5,000,000 measured messages with zero missing or duplicate deliveries.** One additional pre-change Redis baseline process aborted with `Internal CLR error (0x80131506)`; it was recorded and repeated once. The dump remains local. No alternate runtime was used.
- All 36 notification runs completed with zero remote duplicates; dropped best-effort events are reported above.

Validation: Foundatio build and **2,221 tests passed**, with 24 expected skips; Mediator build with zero warnings and **756 tests passed**; frontend checks/build, **23 browser scenarios** with separate API/worker processes, and both executable console walkthroughs passed. The existing Foundatio AppHost ASPIRE010 warning remains.

Reproduce queue cases with `compare.ps1` (`-Redis`, `-Aws`, `-Tracking`, `-Concurrency`, `-Messages`); notification cases use the sibling `Foundatio.Mediator.Notification.Benchmarks` project. See their READMEs for commands.
