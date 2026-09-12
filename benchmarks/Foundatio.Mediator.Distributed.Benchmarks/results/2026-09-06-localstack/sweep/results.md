# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/local/pubsub/p128/s8/c10/f1/b200000/r0/w0 | 3/3 | 6847406 (6141518–11069026) | 0.001 | 0 / 600000 | 0 |
| foundatio/local/queue/p128/s8/c8/f1/b200000/r0/w0 | 3/3 | 8560412 (6903862–13029262) | 0.001 | 0 / 600000 | 0 |
| foundatio/memory/pubsub/p128/s1/c10/f1/b50000/r0/w0 | 3/3 | 282019 (259352–342792) | 140.280 | 0 / 150000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b1000/r0/w0 | 3/3 | 304920 (244227–340287) | 2.872 | 10779 / 15000 | 10779 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (250–250) | 0.195 | 0 / 6000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b50000/r0/w0 | 3/3 | 248516 (222491–256069) | 173.940 | 0 / 150000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f3/b50000/r0/w0 | 3/3 | 175443 (90986–197362) | 263.701 | 0 / 450000 | 0 |
| foundatio/memory/pubsub/p4096/s8/c10/f1/b50000/r0/w0 | 3/3 | 108517 (101641–123519) | 424.771 | 0 / 150000 | 0 |
| foundatio/memory/queue/p128/s1/c8/f1/b50000/r0/w0 | 3/3 | 178729 (167859–180503) | 0.668 | 0 / 150000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b50000/r0/w0 | 3/3 | 174939 (141971–197856) | 156.956 | 0 / 150000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b50000/r0/w250 | 3/3 | 27865 (27805–27984) | 1665.108 | 0 / 150000 | 0 |
| foundatio/memory/queue/p4096/s8/c8/f1/b50000/r0/w0 | 3/3 | 108318 (67522–110149) | 211.494 | 0 / 150000 | 0 |
| foundatio/sqs/pubsub/p128/s1/c10/f1/b2000/r0/w0 | 3/3 | 152 (144–159) | 13027.137 | 0 / 6000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b1000/r0/w0 | 3/3 | 147 (79–160) | 6778.727 | 11997 / 15000 | 11997 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 2/3 | 153 (147–159) | 5093.822 | 468 / 6000 | 468 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b2000/r0/w0 | 3/3 | 141 (99–149) | 14113.295 | 0 / 6000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f3/b2000/r0/w0 | 3/3 | 87 (75–104) | 22796.090 | 0 / 18000 | 0 |
| foundatio/sqs/pubsub/p4096/s8/c10/f1/b2000/r0/w0 | 3/3 | 160 (158–161) | 12399.812 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p128/s1/c8/f1/b2000/r0/w0 | 3/3 | 381 (380–390) | 6.940 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b2000/r0/w0 | 3/3 | 447 (438–508) | 1898.767 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b2000/r0/w250 | 3/3 | 401 (278–423) | 2291.678 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p4096/s8/c8/f1/b2000/r0/w0 | 3/3 | 377 (237–573) | 2332.069 | 0 / 6000 | 0 |
| masstransit/memory/pubsub/p128/s1/c10/f1/b50000/r0/w0 | 3/3 | 38203 (30514–39009) | 802.756 | 0 / 150000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (250–250) | 0.289 | 0 / 6000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b50000/r0/w0 | 3/3 | 36956 (36890–38984) | 1041.619 | 0 / 150000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f3/b50000/r0/w0 | 3/3 | 34722 (27512–36566) | 1135.130 | 0 / 450000 | 0 |
| masstransit/memory/pubsub/p4096/s8/c10/f1/b50000/r0/w0 | 3/3 | 33978 (33287–35393) | 1130.492 | 0 / 150000 | 0 |
| masstransit/memory/queue/p128/s1/c8/f1/b50000/r0/w0 | 3/3 | 37902 (28679–40275) | 887.027 | 0 / 150000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b50000/r0/w0 | 3/3 | 41186 (40755–42189) | 1022.746 | 0 / 150000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b50000/r0/w250 | 3/3 | 3811 (3608–3811) | 12811.525 | 0 / 150000 | 0 |
| masstransit/memory/queue/p4096/s8/c8/f1/b50000/r0/w0 | 3/3 | 32709 (23412–38173) | 1150.071 | 0 / 150000 | 0 |
| masstransit/sqs/pubsub/p128/s1/c10/f1/b2000/r0/w0 | 3/3 | 225 (205–228) | 14.752 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 241 (235–250) | 1038.888 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b2000/r0/w0 | 3/3 | 938 (504–1052) | 273.187 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f3/b2000/r0/w0 | 3/3 | 332 (208–333) | 2131.192 | 0 / 18000 | 0 |
| masstransit/sqs/pubsub/p4096/s8/c10/f1/b2000/r0/w0 | 3/3 | 805 (687–823) | 531.805 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p128/s1/c8/f1/b2000/r0/w0 | 3/3 | 302 (215–310) | 5.969 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b2000/r0/w0 | 3/3 | 1123 (729–1227) | 628.173 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b2000/r0/w250 | 3/3 | 1181 (492–1226) | 544.786 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p4096/s8/c8/f1/b2000/r0/w0 | 3/3 | 782 (730–1067) | 677.451 | 0 / 6000 | 0 |
