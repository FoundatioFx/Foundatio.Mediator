# Queue allocation and recovery validation — September 8, 2026

This follow-up compares PR #149 (`89bd6d1`), the previous native implementation (Mediator `ff9c155`, Foundatio `54006ac7`), and the source fingerprints in `manifest.json`. Mediator core still matches main `a148013` exactly. All runs use the normal Ubuntu `/usr/bin/dotnet`, .NET 10.0.11, Release, on the same shared Linux development host. Redis 7 and LocalStack 3.8.1 run locally.

## What changed

- Ordinary single-message sends avoid batch-outcome bookkeeping on successful acceptance; failure outcomes retain application IDs and provider details.
- Header builders share immutable snapshots until the first edit. Automatic settlement and unused lease renewal avoid allocating semaphores.
- In-memory receipts use private object identity instead of a GUID string per delivery; stale receipts still cannot settle or renew redeliveries.
- Redis cancellation expires and reads the requested job atomically in one round trip.
- The Mediator extension caches immutable enqueue registration metadata and avoids empty header-provider enumeration. Every invocation still runs the normal pipeline and resolves its own scoped services.

No application API changes are required. Cancellation, lease supervision, serialization, retry behavior and at-least-once delivery remain enabled.

## Queue throughput and allocations

| Scenario | PR #149 jobs/s | Before jobs/s | After jobs/s | Allocated bytes/job, before → after |
| --- | ---: | ---: | ---: | ---: |
| In memory, concurrency 64 | 185,107 | 100,073 | 97,236 | 9,847 → 8,353 |
| In memory, tracked, concurrency 64 | 37,120 | 31,049 | 30,584 | 17,049 → 15,533 |
| In memory, concurrency 1 | 102,281 | 81,347 | 87,539 | 10,195 → 8,685 |
| In memory, concurrency 8 | 180,014 | 80,149 | 84,685 | 9,852 → 8,333 |
| SQS / LocalStack, concurrency 64 | 2,883 | 2,809 | 2,846 | 48,011 → 47,013 |
| In memory + Redis tracking, concurrency 64 | 8,428 | 5,898 | 5,409 | 47,360 → 45,831 |
| SQS / LocalStack + Redis tracking, concurrency 64 | 2,414 | 2,498 | 2,445 | 85,479 → 84,388 |

The clearest gain is allocation reduction: **15% per untracked in-memory job**, **9% with in-memory tracking**, and **3% with Redis tracking**. In-memory throughput improves **8% at concurrency 1** and **6% at concurrency 8**. Concurrency 64 and tracked memory are near the baseline (2.8% and 1.5% lower respectively); LocalStack is also near the baseline. These runs do not establish gains in those scenarios. PR #149 remains faster and leaner in memory.

Redis short runs disagreed: the first final-code exploratory batch favored the change, while the matrix above measured 8.3% lower throughput. Five additional alternating pairs with **30,000 jobs per run** measured **6,380 → 6,503 jobs/s** (+1.9%), **47,323 → 45,839 bytes/job**, and **16,474 → 14,290 ms of process CPU** (13% less). Acceptance p99 was **11.34 → 10.71 ms**. The defensible conclusion is lower allocation/CPU cost with no consistent throughput gain. The longer runs are recorded separately and do not replace the matrix's Redis row.

## Latency

Medians of each run's p99, milliseconds. Handler entry includes queue waiting and excludes handler execution and final settlement.

| Scenario | Acceptance p99, before → after | Handler entry p99, before → after |
| --- | ---: | ---: |
| In memory, concurrency 64 | 2.41 → 0.20 | 1177.01 → 1273.38 |
| In memory, tracked, concurrency 64 | 4.96 → 5.45 | 995.34 → 1040.69 |
| In memory, concurrency 1 | 0.01 → 0.01 | 354.44 → 334.36 |
| In memory, concurrency 8 | 0.04 → 0.03 | 791.03 → 739.14 |
| SQS / LocalStack, concurrency 64 | 31.24 → 31.84 | 907.09 → 769.79 |
| In memory + Redis tracking, concurrency 64 | 12.40 → 13.10 | 739.59 → 813.67 |
| SQS / LocalStack + Redis tracking, concurrency 64 | 33.10 → 32.39 | 1430.38 → 1483.24 |

## Recovery under continuing arrivals

An isolated two-minute LocalStack/Redis run accepted **46,504 jobs**: **46,464 completed**, **20 were cancelled while queued**, and **20 while running**. Running cancellation finished in 4.96 seconds with the existing five-second polling interval.

The harness killed a worker process with **32 confirmed handler invocations in flight**, started a replacement and a second worker, then gracefully stopped/restarted the second worker while arrivals continued. All 32 interrupted jobs retried. No jobs remained queued, processing, failed, dead-lettered or acceptance-unknown. There were 46,516 handler invocations, with zero duplicate effect attempts in this run. The handler uses an idempotent Redis write: this validates recovery, not an exactly-once guarantee.

The surviving worker's working set was 123 MB near cancellation and 131 MB after drain. This short run is not a long-duration memory-leak study. `recovery-results.json` contains the counts and process snapshots; `Recovery.Program.cs.txt` and `Recovery.csproj.txt` preserve the harness.

## Notification check

Single validation runs delivered all 10,000 selected cluster events with zero remote duplicates in both paced traffic and traffic mixed with one million local-only events (capacity 10,000). One million local-only events with the bridge enabled took 54 ms. These are smoke checks, not a new statistical notification benchmark. The [previous report](../optimization-2026-09-08) retains the full overload/buffering comparison; best-effort broadcasts can still drop events.

## Method and evidence

- Three fresh-process repetitions per cell, rotating implementation order, with a 1,000-message warmup and a 256-character payload. Counts: 200,000 memory/concurrency 64; 100,000 at concurrency 1/8; 50,000 memory tracked; 10,000 with Redis and/or LocalStack.
- Timing includes enqueue and broker drain; tracked runs also wait for retained completion counts. Startup, warmup and shutdown are excluded. SQS drain relies on approximate statistics. These no-op handler measurements are not core-dispatch benchmarks or production AWS capacity estimates.
- `matrix-results.json` records **63 successful runs and 4,320,000 measured messages**, with zero missing or duplicate deliveries. One additional PR #149 process hit the previously observed `Internal CLR error (0x80131506)` and passed its one retry. The failed trial and stderr remain in the evidence; the dump stays local.
- `redis-validation-results.json` records the ten longer Redis runs. `quick-v2-results.json` records the first exploratory final-code runs; `quick-results.json` records an earlier allocation-only version before the receipt/lease changes. No exploratory runs are silently pooled into the final table.
- One Mediator build also hit the known CLR abort and passed on the same-runtime retry. `mediator-v2-build.log` retains the failure. No runtime installation was changed.
- The sample passed all **23 browser scenarios** against isolated API and worker processes. The Mediator console walkthrough completed validation, enqueue, progress and tracked completion.
- Foundatio full build and **2,229 tests passed**, with 24 existing skips and the existing AppHost ASPIRE010 warning. Mediator full build has zero warnings and **756 tests passed**. Coverage includes immutable header snapshots, partial acceptance, exact cancellation expiry, overlapping settlement, stale receipts, lease behavior and repeated scoped header restoration.

To repeat queue cases, use the parent `compare.ps1` with `-Messages`, `-Concurrency`, `-Tracking`, `-Redis` and `-Aws`. Native/PR149 runner sources, orchestration scripts and hashes are preserved here. For recovery, copy the two `Recovery.*.txt` files to `Program.cs` and `Recovery.csproj` in a separate directory, build with `-p:MediatorPath=<checkout> -p:FoundatioCorePath=<Foundatio checkout> -p:GeneratePackageOnBuild=false`, and run the DLL with `--seconds 120 --output <existing output directory>`. Set `BENCHMARK_REDIS_CONNECTION_STRING` and `BENCHMARK_AWS_URL` to isolated local services. The harness creates unique resources, starts child processes using system `/usr/bin/dotnet`, and removes its queues afterward.
