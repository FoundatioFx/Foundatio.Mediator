# Distributed transport optimizations: measured results

The transport optimizations substantially improve the default workloads on this host. The final paired measurements, earlier confirmation, and remaining counterexamples are preserved below alongside the [original baseline](../2026-09-06-localstack/README.md).

## Final paired comparisons

Final runtime source: **`c6968207a08404fcaf2d22604c1c118153317bd4`**. Queue workers now skip the capacity-collection timer when there is room for the largest batch observed from their transport. A deterministic clock test verifies that six ten-message batches fill a 64-worker queue without waiting on that timer. Publisher-only hosts can explicitly disable inbound notifications; the harness now uses this role on its producers.

The final pass uses **adjacent framework pairs**: shuffle workload pairs, then randomize framework order within each pair. This reduces time between comparable cases on the shared host. All **40** runs passed, with no loss, duplicates, invalid messages, drops, or transport failures. The table reports completed messages/s, median and observed min–max. Queue prefetch equals consumer concurrency; pub/sub consumers use concurrency ten. Business payload is 128 bytes unless stated. Each burst offers 5,000 messages unless stated; the notification buffer retains the full offered count.

| Workload | Repetitions per framework | Foundatio messages/s (min–max) | MassTransit messages/s (min–max) | Foundatio wins within adjacent pairs |
|---|---:|---:|---:|---:|
| Queue, 8 producers / 8 workers | 3 | 1,642 (1,606–1,800) | 1,279 (1,209–1,325) | 3/3 |
| Queue, 32 producers / 32 workers | 3 | 2,373 (2,131–2,522) | 2,167 (1,882–2,175) | 2/3 |
| Queue, 64 producers / 64 workers | 3 | 2,687 (2,388–2,760) | 2,496 (2,397–2,961) | 1/3 |
| Queue, 4 KiB, 8 producers / 8 workers | 3 | 1,491 (1,405–1,531) | 1,134 (1,125–1,231) | 3/3 |
| Queue, 1 producer / 8 workers, 2,000 messages | 1 | 348 (348–348) | 309 (309–309) | 1/1 |
| Queue, 8 producers / 8 workers, 100,000 messages | 1 | 2,399 (2,399–2,399) | 1,494 (1,494–1,494) | 1/1 |
| Pub/sub, 40 producers | 3 | 1,345 (1,319–1,367) | 1,139 (1,095–1,166) | 3/3 |
| Pub/sub, 4 KiB, 40 producers | 3 | 1,128 (1,084–1,227) | 1,002 (978–1,025) | 3/3 |

At concurrency 64, Foundatio's median is higher, but **MassTransit wins two of the three adjacent pairs**. That is not a consistent Foundatio win. These timings do not support a universal ranking; keep the earlier losing cases below as well. Back-to-back runs reduce one source of variability but do not isolate CPU scheduling or broker emulation. Queue jobs remain untracked, and Foundatio notifications remain best effort while MassTransit publications await transport acceptance. Equal publication concurrency does not make their guarantees identical.

## Publisher-only pass and host interference

The preceding **52-run** publisher-only pass used **`de321952996b266355ab0e0221293254060af5c9`** and offered **1,884,000 messages**, all completed without loss or duplicates. It exercises 128-byte and 4 KiB payloads, 8 and 40 producers, three subscribers, a 250/s offered rate, in-memory delivery, and 100,000-message bursts. Unrelated Elasticsearch validation reached about 300% CPU and another LocalStack workload about 100% CPU during this pass. Both frameworks slowed and the ranges widened. This is evidence for the publisher-only configuration's correctness; it does **not** establish the incremental speedup caused by removing its subscription infrastructure.

| Publisher-only workload | Foundatio messages/s (min–max) | MassTransit messages/s (min–max) |
|---|---:|---:|
| 128 bytes, 8 producers | 949 (619–1,450) | 623 (470–942) |
| 128 bytes, 40 producers | 755 (670–1,611) | 1,004 (574–1,085) |
| 4 KiB, 8 producers | 678 (516–1,211) | 653 (439–897) |
| 4 KiB, 40 producers | 913 (680–1,152) | 463 (461–938) |
| In memory, 250,000 messages | 256,470 (252,477–260,647) | 62,864 (53,311–66,770) |

The **128-byte, 40-producer median favored MassTransit** in that pass; it has not been discarded. That reversal and the wide ranges prompted the adjacent-pair repetition above. Rate-driven runs delivered every message but some fell below the 250/s offered rate; scheduled latency in the raw results retains late admission and backlog. Do not treat a lossless run as proof that every latency target was met.

Across the main confirmation and these two follow-up passes, **294/294** runs passed, with **62,038,000 offered messages**, no unexpected missing or duplicate deliveries, and **23,209 intentional overload drops**. Earlier candidate experiments are archived separately and excluded from those totals.

The [final paired results](receive-capacity/results.md) and [publisher-only results](publisher-only/results.md) retain every run, full settings, assembly hashes, CSV, environment metadata, and exact execution order. The final pass changes only queue receive scheduling relative to the publisher-only pass; notification implementation is unchanged. A dedicated host and real AWS are still required for production performance conclusions.

## Earlier confirmation at c9bcbd9

AWS transport calls now coalesce concurrent sends, acknowledgments, and publications into bounded batches. Each caller still waits for its own broker result; partial failure, cancellation, deadlines, aggregate byte limits, and shutdown are covered by regression tests. Packed headers reduce attribute overhead, and SNS filters redundant self-delivery. Notification publication retains a synchronous in-memory fast path and permits 40 concurrent broker operations. Queue acknowledgments use the received batch size to avoid waiting for ten entries when fewer were received. The worker's in-flight limit and lease guarantees remain intact.

The main confirmation used source **`c9bcbd95d705d075ad907a19e6a2942f449447a8`**, before the explicit publisher-only option. It completed **202/202 valid runs**, offering **59,770,000 messages**. No lossless run had missing, duplicate, invalid, dropped, or failed deliveries. Six deliberate buffer-overload runs accounted for **23,209 drops**; these are excluded from lossless throughput comparisons. Every queue run passed completion, settlement, and broker-drain checks.

## Earlier default workload

Median and observed min–max of **five** shuffled repetitions: 128-byte business payload, eight producers, queue concurrency/prefetch eight, pub/sub consumer concurrency ten, one subscriber. Each broker run offers 5,000 messages; in-memory runs 250,000; local runs 5,000,000. Notification burst buffers hold the offered count. Throughput counts completed business messages, normalized by subscriber count.

| Path | Queue messages/s (min–max) | Pub/sub messages/s (min–max) |
|---|---:|---:|
| Foundatio local mediator | 7,671,895 (6,809,470–8,750,844) | 7,348,616 (6,939,529–8,706,973) |
| Foundatio distributed, in memory | 191,217 (186,939–200,092) | 240,154 (226,710–247,703) |
| MassTransit in memory | 73,208 (72,129–74,167) | 68,563 (66,802–70,885) |
| Foundatio SQS / SNS+SQS | 1,603 (1,239–1,747) | 1,388 (1,374–1,633) |
| MassTransit SQS / SNS+SQS | 1,288 (1,181–1,367) | 951 (904–990) |

The broker medians are **24% higher for queues** and **46% higher for pub/sub** than MassTransit. Foundatio's queue range overlaps MassTransit's, so individual runs can reverse the ranking. Against the original Foundatio baseline, the queue median increased from 555 to 1,603 messages/s and pub/sub from 158 to 1,388. Those before/after sessions were separate shared-host measurements, not a controlled isolated-machine experiment.

The notification defaults differ: Foundatio's eight API producers feed 40 transport operations; MassTransit's eight callers await broker acceptance. With **40 application producers on both sides**, three shuffled repetitions yielded Foundatio **1,405 (1,291–1,560)** versus MassTransit **1,187 (1,062–1,216)** messages/s, an **18%** median advantage. The APIs still have different buffering and delivery guarantees.

## Sustained and scaling checks

The sustained queue test offered **100,000 messages per run**, three repetitions per framework. Foundatio reached **2,460 (2,369–2,471)** messages/s versus **1,794 (1,783–1,834)**, a **37%** median advantage. All six runs completed without loss or duplicates. One additional 100,000-message notification run per framework reached **1,845** versus **1,122** messages/s; a single repetition establishes completion under sustained work, not variability.

Queue scaling uses 5,000 messages, three repetitions, and producer count `max(8, concurrency)`:

| Queue concurrency / prefetch | Foundatio messages/s (min–max) | MassTransit messages/s (min–max) |
|---|---:|---:|
| 1 | 514 (487–515) | 297 (252–303) |
| 32 | 2,188 (2,039–2,325) | 1,772 (1,761–2,069) |
| 64 | 2,261 (2,152–2,452) | 2,750 (2,523–2,848) |

**MassTransit leads at concurrency 64.** Foundatio's producer finished sending well before consumer drain in those runs, so simply increasing send concurrency would not address the observed bottleneck. The transport improvements preserve the existing bound on leased messages; no extra unmonitored receive backlog was introduced to improve the score.

The 120-run sweep includes these three-repetition broker cases:

| Workload | Foundatio messages/s (min–max) | MassTransit messages/s (min–max) |
|---|---:|---:|
| Queue, one producer | 346 (328–358) | 311 (307–314) |
| Queue, 4 KiB | 1,319 (1,106–1,587) | 1,137 (1,063–1,142) |
| Queue, 250 µs handler work | 1,411 (1,134–1,771) | 1,188 (1,142–1,226) |
| Pub/sub, 4 KiB | 855 (728–1,262) | 866 (776–889) |
| Pub/sub, three subscribers | 494 (399–512) | 410 (410–425) |

The 4 KiB notification medians are effectively tied with substantial variation, including slower Foundatio repetitions. Fixed-rate notification runs at **250 messages/s** completed all messages on both sides, including the Foundatio service level that failed in the original baseline. Intentional buffer overload still drops notifications and remains explicitly visible in the raw results.

## Broker traffic and latency boundaries

Median broker calls during each 5,000-message standard case, including 200 warmup messages and startup/drain/cleanup:

| Operation | Foundatio queue | MassTransit queue | Foundatio pub/sub | MassTransit pub/sub |
|---|---:|---:|---:|---:|
| SendMessageBatch / PublishBatch | 668 | 665 | 526 | 702 |
| ReceiveMessage | 754 | 651 | 525 | 523 |
| DeleteMessageBatch | 756 | 660 | 520 | 536 |

The original Foundatio queue sent and acknowledged one broker request per message. The new counts demonstrate actual coalescing; the speedup is not just returning earlier. SNS filtering also removes redundant self-delivery. [Broker request counts](broker-requests.json) are grouped by each case's UTC start through the next case's start and can include a trailing cancelled empty poll at that boundary. The dedicated broker serves only this benchmark.

Foundatio notification `PublishAsync` returns after local buffering; MassTransit bus publication waits for broker acceptance. The 100,000-message notification burst therefore has delivery p99 **53,989 ms** for Foundatio versus **10,703 ms** for MassTransit even though Foundatio completes the entire burst sooner: Foundatio admits the burst immediately, while MassTransit holds admission at its callers. Compare scheduled latency at the same offered rate when evaluating application response-time requirements. Do not compare their API latency as equivalent guarantees. Notification delivery is best effort; queues are the durable path.

## Environment and reproducibility

- September 6, 2026; AMD Ryzen AI 9 HX 470, 12 cores / 24 logical processors, boost enabled, no CPU affinity.
- Ubuntu 26.04.1 LTS, kernel 7.0.0-31, SDK 10.0.111 / runtime 10.0.11, workstation GC.
- MassTransit **8.5.10**, AWS SDK SQS 4.0.2.24 / SNS 4.0.2.26. Version 9.2.0 was not benchmarked because the setup probe required a bus license.
- LocalStack **3.8.1**, image digest `sha256:b279c01f4cfb8f985a482e4014cabc1e2697b9d7a6c8c8db2e40f4d9f93687c7`, isolated container on port 14566.
- Shared development host; unrelated containers remained running. Our sample, builds, tests, and profilers were stopped during measurements. Min–max ranges are observed variation, not confidence intervals.
- Broker consumers run in separate .NET processes on the same host. Broker CPU is excluded from process allocation/CPU counters. Different native wire envelopes remain enabled.

These results establish performance on this **LocalStack/shared-host configuration**, not real AWS throughput, cost, or a universal ranking. Repeat representative sustained offered-rate workloads on AWS with the intended delivery guarantees before setting production targets.

See the [harness README](../../README.md) for setup and exact measurement boundaries. After the Release build and dedicated broker startup, from the repository root in PowerShell:

```powershell
$runner = 'benchmarks/Foundatio.Mediator.Distributed.Benchmarks/bin/Release/net10.0/Foundatio.Mediator.Distributed.Benchmarks.dll'
dotnet $runner matrix --suite standard --count 5000 --local-count 5000000 --memory-count 250000 --repetitions 5 --endpoint http://localhost:14566 --timeout 300 --output ./BenchmarkDotNet.Artifacts/distributed/standard
dotnet $runner matrix --suite sweep --count 2000 --local-count 200000 --memory-count 50000 --repetitions 3 --endpoint http://localhost:14566 --timeout 300 --output ./BenchmarkDotNet.Artifacts/distributed/sweep
dotnet $runner run --framework foundatio --transport sqs --operation pubsub --count 5000 --capacity 5000 --producers 40 --endpoint http://localhost:14566 --output ./BenchmarkDotNet.Artifacts/distributed/matched
dotnet $runner run --framework masstransit --transport sqs --operation pubsub --count 5000 --capacity 5000 --producers 40 --endpoint http://localhost:14566 --output ./BenchmarkDotNet.Artifacts/distributed/matched

# Replay the final adjacent pairs in their recorded order.
$order = Get-Content 'benchmarks/Foundatio.Mediator.Distributed.Benchmarks/results/2026-09-06-batching/receive-capacity/order.json' -Raw | ConvertFrom-Json
foreach ($case in $order) {
    $caseArgs = @('run', '--transport', 'sqs', '--endpoint', 'http://localhost:14566', '--timeout', '300', '--output', './BenchmarkDotNet.Artifacts/distributed/paired')
    foreach ($property in $case.PSObject.Properties) {
        $caseArgs += "--$($property.Name)"
        $caseArgs += [string]$property.Value
    }
    dotnet $runner @caseArgs
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed; inspect the saved result before continuing.' }
}
```

The [standard](standard/results.md), [sweep](sweep/results.md), [matched producer concurrency](matched/results.md), [sustained](sustained/results.md), and [queue scaling](scale/results.md) directories each retain `runs.json`, CSV, Markdown, and environment/assembly-hash metadata. Exact supplemental case orders are in `matched-order.json`, `sustained-order.json`, and `scale-order.json`. Repeat each settings row with the corresponding `run` options to reconstruct those workloads. [Validation totals](validation.json) preserve all drops and missing-message accounting.

Earlier candidate measurements are retained separately under `interim/`: initial coalescing at `94f42ee1a429fa82685ed316914fca7c35194290`, and the synchronous publication fast path at `578cf7c`. They are **not pooled** into the confirmation medians. In particular, the earlier sustained queue run effectively tied MassTransit; received-batch acknowledgment hints were added before the final repeated queue measurements.
