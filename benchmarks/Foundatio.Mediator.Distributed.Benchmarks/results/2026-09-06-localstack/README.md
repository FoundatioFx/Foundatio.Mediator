# PR 149 distributed performance measurements

Measured on September 6, 2026 against runtime source `1f21e5e6f047f8ba4016a204ad6a1a2e7e1814f0`, using the benchmark harness in this change. No runtime performance changes were made for these measurements. The harness assembly hashes and complete workload settings are archived with each dataset.

The longer baseline completed **30/30 runs**, offering **33,060,000 messages**, with no missing, duplicate, invalid, or dropped deliveries. The exploratory sweep ran **120 cases** and offered **3,750,000 messages**. It preserved one failed lossless fixed-rate run and six intentionally lossy buffer-overload runs. No queue run lost or duplicated a message.

## Longer baseline

Median of three randomized repetitions. Business payload: 128 bytes; eight producers; one subscriber; queue concurrency/prefetch eight; pub/sub concurrency ten. Local runs used 5,000,000 messages, in-memory runs 250,000, and broker runs 5,000. All broker consumers ran in separate .NET processes on the same host. Queue jobs were untracked. Pub/sub burst buffers were sized to retain the offered count.

| Path | Queue messages/s (min–max) | Pub/sub messages/s (min–max) |
|---|---:|---:|
| Foundatio local mediator | 7,235,147 (7,145,690–9,074,520) | 8,181,939 (6,680,044–8,682,305) |
| Foundatio distributed, in memory | 170,536 (168,768–173,866) | 225,959 (121,194–244,662) |
| MassTransit in memory | 73,114 (71,839–74,785) | 68,462 (68,396–68,997) |
| Foundatio SQS / SNS+SQS | 555 (455–570) | 158 (127–172) |
| MassTransit SQS / SNS+SQS | 1,315 (1,304–1,340) | 919 (688–973) |

These are instrumented application throughput measurements. The local mediator's collector, allocation, clock reads, and producer scheduling are included; use the existing BenchmarkDotNet suite for precise dispatch overhead. Counts differ between transport tiers to keep runs practical, so compare latency between frameworks within the same tier and workload. The min/max ranges are observed variation, not confidence intervals.

| Broker path | Median API p99 ms | Median delivery p99 ms | Median allocated bytes/message, producer + consumer |
|---|---:|---:|---:|
| Foundatio queue | 20.781 | 3,651.940 | 119,985 |
| MassTransit queue | 6.725 | 1,102.581 | 65,901 |
| Foundatio pub/sub | 0.027 | 31,401.800 | 170,763 |
| MassTransit pub/sub | 15.008 | 704.642 | 74,422 |

Foundatio's notification API returns after writing its local buffer; MassTransit's bus publication awaits transport acceptance. API latency therefore has different semantics. Delivery latency includes the accumulated burst backlog. Allocations include the SDK, transport, runtime, and harness polling/reporting; they exclude LocalStack.

## What the sweep exposed

- **Default-buffer overload:** all three SQS/SNS burst runs offered 5,000 notifications with capacity 1,000. Each delivered 1,001 and dropped 3,999. The in-memory equivalents dropped 3,341, 3,613, and 3,825. These were explicit `--allow-drops` cases, with every missing delivery accounted for by the actual drop counter. They are not lossless throughput wins.
- **A failed fixed-rate service level:** one of three Foundatio SQS/SNS runs at 250 offered messages/second dropped **468 of 2,000** with the default buffer. Delivered-message p99 was **12.713 seconds**, and achieved delivery throughput was **73 messages/second**. The other repetitions avoided loss but still accumulated seconds of latency. The failed run remains in the raw data and failure counts; timing summaries explicitly use valid runs only.
- **Fan-out and payload cost:** the sweep includes three subscribers, 4 KiB payloads, one producer, and 250 microseconds of CPU work for queue handlers. The full per-case results are linked below. Synchronous CPU work in the MassTransit in-memory case scaled differently from Foundatio; the configured concurrency was eight on both sides. This warrants a dedicated scheduling/profile experiment before generalizing to asynchronous application handlers.
- **Short runs can mislead:** MassTransit in-memory throughput improved substantially in the longer baseline, which is why it supplies the headline table. The sweep is retained as scaling and overload evidence.

## Broker request amplification

LocalStack request logs were grouped between each case's UTC start and the next case's start. These counts include **2,000 measured messages + 200 warmup messages**, startup, drain checks, and cleanup. The dedicated broker served only this harness. Values below are medians of the three corresponding sweep runs; the complete counters are archived in [broker-requests.json](sweep/broker-requests.json).

| Operation, 2,200 offered messages | Foundatio requests | MassTransit requests |
|---|---:|---:|
| Queue send, `SendMessageBatch` | 2,200 | 281 |
| Queue acknowledgment | 2,200 `DeleteMessage` | 278 `DeleteMessageBatch` |
| Queue receive | 661 | 277 |
| SNS publish, `PublishBatch` | 2,200 | 303 |
| Notification receive | 2,208 | 223 |
| Notification acknowledgment, `DeleteMessageBatch` | 2,206 | 227 |

Foundatio's transport accepts batches, but the mediator's individual enqueue and notification-worker calls send single entries. MassTransit coalesces concurrent sends/publications and acknowledgments. Foundatio also has a publisher-side notification subscription that receives and filters its own events, so its notification receive counts include that traffic. The observed request amplification is a concrete optimization target; the exact throughput gain from changing it still needs measurement.

Recommended follow-up work:

1. Drain available outbound notifications into bounded batches, preserving drop accounting, headers, shutdown behavior, and visibility of partial failures.
2. Add bounded SQS send and acknowledgment coalescing. An enqueue receipt must still wait for broker acceptance, and worker settlement must wait for acknowledgment. Test batch partial failures, cancellation, shutdown, and lease expiry before measuring speedups.
3. Provide an explicit notification backpressure or transport-confirmed publication option for applications that need to control loss and latency. Document the buffered best-effort mode at the publishing API.
4. Repeat the matrix on a dedicated host and real AWS with the intended delivery guarantees, representative handler work, and a sustained offered-rate sweep. Keep noisy absolute throughput thresholds out of ordinary CI; the new CI job verifies readiness, accounting, delivery, and cleanup for all ten paths.

## Environment and limits

- AMD Ryzen AI 9 HX 470, 12 cores / 24 logical processors, frequency boost enabled, unrestricted CPU affinity.
- Ubuntu 26.04.1 LTS, kernel 7.0.0-31, .NET SDK 10.0.111 / runtime 10.0.11, workstation GC.
- MassTransit **8.5.10**, AWS SDK SQS 4.0.2.24 / SNS 4.0.2.26. MassTransit 9.2.0 bus startup required a license in the setup probe; it was not benchmarked.
- LocalStack **3.8.1**, image digest `sha256:b279c01f4cfb8f985a482e4014cabc1e2697b9d7a6c8c8db2e40f4d9f93687c7`, isolated Docker container on loopback port 14566.
- This was a **shared development host**. Other Exceptionless tests and Elasticsearch work became CPU-heavy during the exploratory sweep. They were quieter before the longer baseline, but were neither stopped nor isolated. The wide ranges, especially in-memory pub/sub, must remain visible. These results support engineering investigation, not a universal framework ranking.
- LocalStack's own CPU and emulation behavior limit broker throughput. **No real AWS capacity or cost claim follows from these numbers.** The same transport APIs are exercised, but the frameworks retain different envelopes, batching, and notification guarantees.

## Reproduce and inspect

See the [harness README](../../README.md) for infrastructure, version selection, exact timing boundaries, and AWS configuration. From the repository root, after the Release build and Compose startup:

```powershell
$runner = 'benchmarks/Foundatio.Mediator.Distributed.Benchmarks/bin/Release/net10.0/Foundatio.Mediator.Distributed.Benchmarks.dll'
dotnet $runner matrix --suite standard --count 5000 --local-count 5000000 --memory-count 250000 --repetitions 3 --endpoint http://localhost:14566 --timeout 180 --output ./BenchmarkDotNet.Artifacts/distributed/baseline
dotnet $runner matrix --suite sweep --count 2000 --local-count 200000 --memory-count 50000 --repetitions 3 --endpoint http://localhost:14566 --timeout 180 --output ./BenchmarkDotNet.Artifacts/distributed/sweep
```

The sweep command can return nonzero when a lossless offered-rate case exceeds available capacity, as it did here. Preserve that result instead of retrying until it disappears.

- Baseline: [complete runs](baseline/runs.json), [CSV](baseline/results.csv), [summary](baseline/results.md), [environment](baseline/environment.json).
- Sweep: [complete runs](sweep/runs.json), [CSV](sweep/results.csv), [summary including the failure](sweep/results.md), [environment](sweep/environment.json).

Use `report --output <archived-directory>` to regenerate summaries from either `runs.json` bundle.
