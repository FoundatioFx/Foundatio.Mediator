# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/sqs/queue/p128/s32/c32/f1/b1000/r0/w0 | 3/3 | 2188 (2039–2325) | 1125.934 | 0 / 15000 | 0 |
| foundatio/sqs/queue/p128/s64/c64/f1/b1000/r0/w0 | 3/3 | 2261 (2152–2452) | 1201.100 | 0 / 15000 | 0 |
| foundatio/sqs/queue/p128/s8/c1/f1/b1000/r0/w0 | 3/3 | 514 (487–515) | 7241.913 | 0 / 15000 | 0 |
| masstransit/sqs/queue/p128/s32/c32/f1/b1000/r0/w0 | 3/3 | 1772 (1761–2069) | 1148.500 | 0 / 15000 | 0 |
| masstransit/sqs/queue/p128/s64/c64/f1/b1000/r0/w0 | 3/3 | 2750 (2523–2848) | 807.364 | 0 / 15000 | 0 |
| masstransit/sqs/queue/p128/s8/c1/f1/b1000/r0/w0 | 3/3 | 297 (252–303) | 14112.350 | 0 / 15000 | 0 |
