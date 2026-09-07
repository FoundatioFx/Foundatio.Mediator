# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/sqs/pubsub/p128/s40/c10/f1/b5000/r0/w0 | 3/3 | 1345 (1319–1367) | 3701.872 | 0 / 15000 | 0 |
| foundatio/sqs/pubsub/p4096/s40/c10/f1/b5000/r0/w0 | 3/3 | 1128 (1084–1227) | 4416.404 | 0 / 15000 | 0 |
| foundatio/sqs/queue/p128/s1/c8/f1/b2000/r0/w0 | 1/1 | 348 (348–348) | 4.918 | 0 / 2000 | 0 |
| foundatio/sqs/queue/p128/s32/c32/f1/b5000/r0/w0 | 3/3 | 2373 (2131–2522) | 1024.968 | 0 / 15000 | 0 |
| foundatio/sqs/queue/p128/s64/c64/f1/b5000/r0/w0 | 3/3 | 2687 (2388–2760) | 1026.051 | 0 / 15000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b100000/r0/w0 | 1/1 | 2399 (2399–2399) | 2109.173 | 0 / 100000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 3/3 | 1642 (1606–1800) | 488.300 | 0 / 15000 | 0 |
| foundatio/sqs/queue/p4096/s8/c8/f1/b5000/r0/w0 | 3/3 | 1491 (1405–1531) | 168.254 | 0 / 15000 | 0 |
| masstransit/sqs/pubsub/p128/s40/c10/f1/b5000/r0/w0 | 3/3 | 1139 (1095–1166) | 2461.953 | 0 / 15000 | 0 |
| masstransit/sqs/pubsub/p4096/s40/c10/f1/b5000/r0/w0 | 3/3 | 1002 (978–1025) | 3181.512 | 0 / 15000 | 0 |
| masstransit/sqs/queue/p128/s1/c8/f1/b2000/r0/w0 | 1/1 | 309 (309–309) | 5.749 | 0 / 2000 | 0 |
| masstransit/sqs/queue/p128/s32/c32/f1/b5000/r0/w0 | 3/3 | 2167 (1882–2175) | 957.574 | 0 / 15000 | 0 |
| masstransit/sqs/queue/p128/s64/c64/f1/b5000/r0/w0 | 3/3 | 2496 (2397–2961) | 778.919 | 0 / 15000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b100000/r0/w0 | 1/1 | 1494 (1494–1494) | 18123.236 | 0 / 100000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 3/3 | 1279 (1209–1325) | 1117.144 | 0 / 15000 | 0 |
| masstransit/sqs/queue/p4096/s8/c8/f1/b5000/r0/w0 | 3/3 | 1134 (1125–1231) | 761.543 | 0 / 15000 | 0 |
