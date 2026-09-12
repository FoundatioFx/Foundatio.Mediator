# Production quality pass — September 12, 2026

Foundatio #533 and Mediator #335 are rebased on main (`99901a48` / `3f76a7e`). Mediator's existing runtime, generator and core tests still match main exactly.

## What changed

Short deliveries now use a timer for their first lease check. The asynchronous renewal loop starts only when needed, avoiding its allocation and cancellation work on every completed message. Expiry supervision remains active even while a handler blocks. Six regressions cover early settlement, renewal failure/retry, hanging renewal, expiry with and without renewal, and settlement racing the first timer callback.

Release solution builds previously selected Debug configuration for external Foundatio project references. The references now preserve the caller's configuration, including transitive native dependencies. CI builds/tests Release and runs a small benchmark smoke test that rejects unoptimized assemblies. This also fixes the sample's deployed build output.

Operations documentation now uses native `JobState`, accurately describes the two tracing sources and parent relationships, and states that bounded shutdown acknowledgements can still be followed by redelivery. Application APIs and Mediator core are unchanged.

## End-to-end comparison

Median jobs/second, Release, **system .NET 10.0.12** (SDK 10.0.112), shared Linux host. Before is the previous implementation (`7e6e0a19` Foundatio / `673102f` Mediator), rebased without behavioral changes. The runtime change was committed as `e583f953`; the dependency lock records the final documentation-inclusive revision. PR #149 is fixed at `89bd6d1`.

| Workload | PR #149 jobs/s | Before jobs/s | After jobs/s | Allocated bytes/job, before → after |
| --- | ---: | ---: | ---: | ---: |
| In memory, concurrency 64 | 181,195 | 95,696 | 100,346 | 8,358 → 7,928 |
| In memory, tracked, concurrency 64 | 36,658 | 33,238 | 34,111 | 15,535 → 15,146 |
| In memory, concurrency 1 | 110,805 | 93,045 | 100,537 | 8,686 → 8,271 |
| In memory, concurrency 8 | 177,069 | 84,916 | 91,161 | 8,340 → 7,934 |
| SQS / LocalStack, concurrency 64 | 2,788 | 3,065 | 2,968 | 47,026 → 46,623 |
| In memory + Redis tracking, concurrency 64 | 10,619 | 7,757 | 7,600 | 45,852 → 45,450 |
| SQS / LocalStack + Redis tracking, concurrency 64 | 2,702 | 2,653 | 2,582 | 84,285 → 83,930 |

Untracked memory throughput improved **5% at concurrency 64, 8% at concurrency 1, and 7% at concurrency 8**, with **about 5% fewer allocated bytes**. In-memory tracked allocations fell about 2.5%. At concurrency 64, untracked process CPU fell 17%. PR #149 remains faster and leaner in memory. SQS and Redis throughput did not improve in this matrix; their medians were 2–3% lower despite slightly lower CPU and allocation cost.

Each cell has three rotating fresh-process trials, a 1,000-message warmup and a 256-character payload. Counts: 200,000 memory/concurrency 64; 100,000 concurrency 1/8; 50,000 memory tracked; 10,000 Redis and/or LocalStack. Timing includes send, broker drain and final tracked completion. Latency stops at handler entry. All **63 runs and 4.32M measured jobs** completed without missing or duplicate deliveries. Startup, warmup and shutdown are excluded. Renewal remained enabled. LocalStack 3.8.1 and Redis 7 are local test services, not production capacity evidence.

## Longer checks and normal defaults

Five alternating pairs per workload, separately from the matrix:

| Additional check | Before jobs/s | After jobs/s | CPU ms, before → after |
| --- | ---: | ---: | ---: |
| 90,000 jobs, in-memory tracking | 35,092 | 35,025 | 9,929 → 9,394 |
| 30,000 jobs, Redis tracking | 7,656 | 7,341 | 16,135 → 15,337 |
| 200,000 jobs, default 1 ms receive delay | 97,010 | 103,928 | 11,237 → 9,560 |

Longer in-memory tracking runs were effectively level. Redis's median was **4% lower**, with about **5% less process CPU** and **1% fewer allocated bytes**; individual pairs ranged from +0.6% to -12.7%. This does not establish a Redis throughput improvement. The normal receive-delay configuration improved about **7%**, with **15% less CPU**. These 30 runs checked another **3.2M jobs**, with zero missing or duplicate deliveries.

## Layer isolation

Three fresh-process runs per layer, 200,000 untracked in-memory deliveries at concurrency 64:

| Layer | Bytes/job before | Bytes/job after |
| --- | ---: | ---: |
| transport | 2,824 | 2,810 |
| bus | 4,870 | 4,250 |
| execution | 6,590 | 6,122 |
| mediator | 8,381 | 7,925 |

The largest removed overhead was in supervised receiving. The transport case does not provide bus supervision, and the other layers add different metadata, scopes and execution semantics. Do not subtract their timings as isolated costs or use them to claim the end-to-end gain. Use `--layer transport|bus|execution|mediator` to repeat the diagnosis; the runner README explains each boundary.

## Recovery and validation

A **ten-minute** LocalStack/Redis test accepted **69,354** jobs: **69,314 completed**, **20 cancelled while queued**, and **20 while running**. Killing a worker interrupted **32 deliveries; all 32 retried**. A second worker stopped and restarted gracefully while arrivals continued. Final accounting found **zero pending, failed, dead-lettered or acceptance-unknown jobs**, and zero duplicate effect attempts. Running cancellation took 5.04 seconds with a 5-second polling interval. The handler uses idempotent effects; this does not imply exactly-once execution. Producer batches of 25 every 200 ms bound load and keep retained history below its 100,000-record limit.

Full Release builds pass. Foundatio: **2,235 tests passed**, 24 existing skips and the existing AppHost ASPIRE010 warning. Mediator: **756 tests passed**, zero build warnings. The corrected Release API/worker passed **23 browser scenarios**. Frontend checks/build, docs links, Quickstart verification and the console workflow passed.

The [manifest](manifest.json) records binary and source fingerprints. Raw JSON, scripts and the recovery harness are alongside this report. Before/after benchmark binaries were frozen before the final measurements. The earlier [mixed-build trials](mixed-build-excluded) are retained but excluded: PE metadata confirmed Debug native dependencies in that Release output. The initial browser launch used a missing cached Chromium path; the successful runs used the installed Chromium. No .NET installation was changed, and the final measured trials had no CLR aborts.

## Release boundary

This is stronger local evidence, including recovery across processes. It does not cover real AWS network behavior, deployment IAM, long outages, or a multi-hour production-shaped soak. Before shipping, run those scenarios in staging with the intended handler workload, persistence settings and replica count. Enqueue is not a transaction with business data, history retention is not permanent deduplication, and broadcasts remain best effort. Keep those boundaries visible to application developers.
