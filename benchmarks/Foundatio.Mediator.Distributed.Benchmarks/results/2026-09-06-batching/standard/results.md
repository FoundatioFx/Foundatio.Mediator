# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/local/pubsub/p128/s8/c10/f1/b5000000/r0/w0 | 5/5 | 7348616 (6939529–8706973) | 0.001 | 0 / 25000000 | 0 |
| foundatio/local/queue/p128/s8/c8/f1/b5000000/r0/w0 | 5/5 | 7671895 (6809470–8750844) | 0.001 | 0 / 25000000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 5/5 | 240154 (226710–247703) | 923.231 | 0 / 1250000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b250000/r0/w0 | 5/5 | 191217 (186939–200092) | 611.883 | 0 / 1250000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 5/5 | 1388 (1374–1633) | 3585.150 | 0 / 25000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 5/5 | 1603 (1239–1747) | 709.576 | 0 / 25000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 5/5 | 68563 (66802–70885) | 2355.463 | 0 / 1250000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b250000/r0/w0 | 5/5 | 73208 (72129–74167) | 2224.015 | 0 / 1250000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 5/5 | 951 (904–990) | 618.784 | 0 / 25000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 5/5 | 1288 (1181–1367) | 1115.631 | 0 / 25000 | 0 |
