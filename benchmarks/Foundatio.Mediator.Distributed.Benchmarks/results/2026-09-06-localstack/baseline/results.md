# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/local/pubsub/p128/s8/c10/f1/b5000000/r0/w0 | 3/3 | 8181939 (6680044–8682305) | 0.001 | 0 / 15000000 | 0 |
| foundatio/local/queue/p128/s8/c8/f1/b5000000/r0/w0 | 3/3 | 7235147 (7145690–9074520) | 0.001 | 0 / 15000000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 3/3 | 225959 (121194–244662) | 977.978 | 0 / 750000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b250000/r0/w0 | 3/3 | 170536 (168768–173866) | 734.026 | 0 / 750000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 3/3 | 158 (127–172) | 31401.800 | 0 / 15000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 3/3 | 555 (455–570) | 3651.940 | 0 / 15000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 3/3 | 68462 (68396–68997) | 2241.501 | 0 / 750000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b250000/r0/w0 | 3/3 | 73114 (71839–74785) | 2292.957 | 0 / 750000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 3/3 | 919 (688–973) | 704.642 | 0 / 15000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 3/3 | 1315 (1304–1340) | 1102.581 | 0 / 15000 | 0 |
