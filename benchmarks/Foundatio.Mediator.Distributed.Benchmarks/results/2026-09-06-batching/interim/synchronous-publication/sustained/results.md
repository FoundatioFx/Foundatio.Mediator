# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/sqs/pubsub/p128/s8/c10/f1/b100000/r0/w0 | 1/1 | 1783 (1783–1783) | 55899.917 | 0 / 100000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b100000/r0/w0 | 1/1 | 1805 (1805–1805) | 15218.684 | 0 / 100000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b100000/r0/w0 | 1/1 | 1169 (1169–1169) | 9457.414 | 0 / 100000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b100000/r0/w0 | 1/1 | 1834 (1834–1834) | 15180.955 | 0 / 100000 | 0 |
