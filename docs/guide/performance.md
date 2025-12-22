# Performance

Foundatio.Mediator aims to get as close to direct method call performance as possible while providing a full-featured mediator with excellent developer ergonomics. Through C# interceptors and source generators, we eliminate runtime reflection entirely.

## Benchmark Results

> 📊 **Last Updated:** 2025-12-22

### Commands

Fire-and-forget dispatch with no return value.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| Direct_Command                       |      13.76 ns |       0 B |
| MediatorNet_Command                  |      16.78 ns |       0 B |
| MediatR_Command                      |     115.07 ns |     128 B |
| Foundatio_Command                    |     166.59 ns |     200 B |
| Wolverine_Command                    |     514.79 ns |     704 B |
| MassTransit_Command                  |   3,200.25 ns |   4,184 B |

### Queries

Request/response dispatch returning an Order object.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| Direct_Query                         |      60.42 ns |     192 B |
| Direct_QueryWithDependencies         |      80.06 ns |     264 B |
| MediatorNet_Query                    |      61.15 ns |     120 B |
| MediatR_Query                        |     146.99 ns |     320 B |
| Foundatio_Query                      |     219.72 ns |     464 B |
| Wolverine_Query                      |     721.84 ns |   1,000 B |
| MassTransit_Query                    |  12,965.13 ns |  12,488 B |

### Events (Publish)

Notification dispatched to 2 handlers.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| Direct_Event                         |      13.71 ns |       0 B |
| MediatorNet_Publish                  |      19.56 ns |       0 B |
| MediatR_Publish                      |     266.13 ns |     792 B |
| Foundatio_Publish                    |     322.47 ns |     336 B |
| Wolverine_Publish                    |   2,138.78 ns |   1,688 B |
| MassTransit_Publish                  |   5,249.18 ns |   6,008 B |

### Full Query (Dependencies + Middleware)

Query where handler has an injected service (IOrderService) and timing middleware (Before/Finally or IPipelineBehavior).

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| MediatorNet_FullQuery                |      90.24 ns |     192 B |
| MediatR_FullQuery                    |     356.76 ns |     744 B |
| Foundatio_FullQuery                  |     484.14 ns |     776 B |
| Wolverine_FullQuery                  |     714.50 ns |   1,000 B |
| MassTransit_FullQuery                |  13,004.54 ns |  12,560 B |

### Cascading Messages

CreateOrder returns an Order and publishes OrderCreatedEvent to 2 handlers. Foundatio uses tuple returns for automatic cascading; other libraries publish manually.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| MediatorNet_CascadingMessages        |      88.55 ns |     144 B |
| MediatR_CascadingMessages            |     516.09 ns |   1,168 B |
| Foundatio_CascadingMessages          |     297.90 ns |     568 B |
| Wolverine_CascadingMessages          |   3,494.41 ns |   2,912 B |
| MassTransit_CascadingMessages        |  40,247.18 ns |  18,808 B |

### Short-Circuit Middleware (Foundatio Only)

Middleware returns cached result; handler is never invoked. Useful for caching or authorization.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| Foundatio_ShortCircuit               |     164.74 ns |     368 B |

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```