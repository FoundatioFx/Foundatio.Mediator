# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 3/3 | 256470 (252477–260647) | 850.689 | 0 / 750000 | 0 |
| foundatio/sqs/pubsub/p128/s40/c10/f1/b5000/r0/w0 | 3/3 | 755 (670–1611) | 6611.350 | 0 / 15000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b100000/r0/w0 | 1/1 | 1839 (1839–1839) | 54173.989 | 0 / 100000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b2000/r250/w0 | 3/3 | 250 (245–250) | 90.916 | 0 / 6000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 5/5 | 949 (619–1450) | 5254.996 | 0 / 25000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f3/b2000/r0/w0 | 3/3 | 489 (254–553) | 4074.088 | 0 / 18000 | 0 |
| foundatio/sqs/pubsub/p4096/s40/c10/f1/b5000/r0/w0 | 3/3 | 913 (680–1152) | 5459.355 | 0 / 15000 | 0 |
| foundatio/sqs/pubsub/p4096/s8/c10/f1/b5000/r0/w0 | 5/5 | 678 (516–1211) | 7340.813 | 0 / 25000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 3/3 | 62864 (53311–66770) | 2400.393 | 0 / 750000 | 0 |
| masstransit/sqs/pubsub/p128/s40/c10/f1/b5000/r0/w0 | 3/3 | 1004 (574–1085) | 2978.015 | 0 / 15000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b100000/r0/w0 | 1/1 | 771 (771–771) | 17301.221 | 0 / 100000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b2000/r250/w0 | 3/3 | 237 (237–250) | 2524.912 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 5/5 | 623 (470–942) | 1503.213 | 0 / 25000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f3/b2000/r0/w0 | 3/3 | 238 (206–356) | 3539.658 | 0 / 18000 | 0 |
| masstransit/sqs/pubsub/p4096/s40/c10/f1/b5000/r0/w0 | 3/3 | 463 (461–938) | 6149.676 | 0 / 15000 | 0 |
| masstransit/sqs/pubsub/p4096/s8/c10/f1/b5000/r0/w0 | 5/5 | 653 (439–897) | 1610.464 | 0 / 25000 | 0 |
