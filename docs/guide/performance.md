# Performance

Foundatio.Mediator aims to get as close to direct method call performance as possible while providing a full-featured mediator with excellent developer ergonomics. Through C# interceptors and source generators, we eliminate runtime reflection entirely.

## Benchmark Results

> 📊 **Last Updated:** 2025-12-23

### Commands

Fire-and-forget dispatch with no return value.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Command</code></td><td style="text-align:right;white-space:nowrap">3.036 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Command</code></td><td style="text-align:right;white-space:nowrap">5.127 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Command</code></td><td style="text-align:right;white-space:nowrap">11.742 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Command</code></td><td style="text-align:right;white-space:nowrap">54.904 ns</td><td style="text-align:right;white-space:nowrap">128 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Command</code></td><td style="text-align:right;white-space:nowrap">240.127 ns</td><td style="text-align:right;white-space:nowrap">704 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Command</code></td><td style="text-align:right;white-space:nowrap">1,736.321 ns</td><td style="text-align:right;white-space:nowrap">4,192 B</td></tr>
</tbody>
</table>

### Queries

Request/response dispatch returning an Order object.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Query</code></td><td style="text-align:right;white-space:nowrap">31.557 ns</td><td style="text-align:right;white-space:nowrap">120 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Query</code></td><td style="text-align:right;white-space:nowrap">31.946 ns</td><td style="text-align:right;white-space:nowrap">120 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Query</code></td><td style="text-align:right;white-space:nowrap">40.284 ns</td><td style="text-align:right;white-space:nowrap">120 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Query</code></td><td style="text-align:right;white-space:nowrap">77.069 ns</td><td style="text-align:right;white-space:nowrap">320 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Query</code></td><td style="text-align:right;white-space:nowrap">335.164 ns</td><td style="text-align:right;white-space:nowrap">1,000 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Query</code></td><td style="text-align:right;white-space:nowrap">6,436.008 ns</td><td style="text-align:right;white-space:nowrap">12,497 B</td></tr>
</tbody>
</table>

### Events (Publish)

Notification dispatched to 2 handlers.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Publish</code></td><td style="text-align:right;white-space:nowrap">4.265 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Publish</code></td><td style="text-align:right;white-space:nowrap">14.959 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Publish</code></td><td style="text-align:right;white-space:nowrap">21.383 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Publish</code></td><td style="text-align:right;white-space:nowrap">129.381 ns</td><td style="text-align:right;white-space:nowrap">792 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Publish</code></td><td style="text-align:right;white-space:nowrap">2,371.862 ns</td><td style="text-align:right;white-space:nowrap">2,840 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Publish</code></td><td style="text-align:right;white-space:nowrap">2,701.091 ns</td><td style="text-align:right;white-space:nowrap">6,016 B</td></tr>
</tbody>
</table>

### Full Query (Dependencies + Middleware)

Query where handler has an injected service (IOrderService) and timing middleware (Before/Finally or IPipelineBehavior).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>MediatorNet_FullQuery</code></td><td style="text-align:right;white-space:nowrap">55.357 ns</td><td style="text-align:right;white-space:nowrap">192 B</td></tr>
<tr><td style="width:100%"><code>Direct_FullQuery</code></td><td style="text-align:right;white-space:nowrap">85.645 ns</td><td style="text-align:right;white-space:nowrap">304 B</td></tr>
<tr><td style="width:100%"><code>MediatR_FullQuery</code></td><td style="text-align:right;white-space:nowrap">179.436 ns</td><td style="text-align:right;white-space:nowrap">744 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_FullQuery</code></td><td style="text-align:right;white-space:nowrap">252.710 ns</td><td style="text-align:right;white-space:nowrap">776 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_FullQuery</code></td><td style="text-align:right;white-space:nowrap">377.998 ns</td><td style="text-align:right;white-space:nowrap">1,000 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_FullQuery</code></td><td style="text-align:right;white-space:nowrap">6,304.521 ns</td><td style="text-align:right;white-space:nowrap">12,569 B</td></tr>
</tbody>
</table>

### Cascading Messages

CreateOrder returns an Order and publishes OrderCreatedEvent to 2 handlers. Foundatio uses tuple returns for automatic cascading; other libraries publish manually.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">30.657 ns</td><td style="text-align:right;white-space:nowrap">144 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">47.621 ns</td><td style="text-align:right;white-space:nowrap">144 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">124.632 ns</td><td style="text-align:right;white-space:nowrap">344 B</td></tr>
<tr><td style="width:100%"><code>MediatR_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">167.340 ns</td><td style="text-align:right;white-space:nowrap">1,168 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">2,396.809 ns</td><td style="text-align:right;white-space:nowrap">4,064 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">7,880.173 ns</td><td style="text-align:right;white-space:nowrap">18,762 B</td></tr>
</tbody>
</table>

### Short-Circuit Middleware

Middleware returns cached result; handler is never invoked. Each library uses its idiomatic short-circuit approach (IPipelineBehavior, HandlerResult.ShortCircuit, HandlerContinuation.Stop, etc.).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">3.099 ns</td><td style="text-align:right;white-space:nowrap">72 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">13.787 ns</td><td style="text-align:right;white-space:nowrap">72 B</td></tr>
<tr><td style="width:100%"><code>MediatR_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">58.267 ns</td><td style="text-align:right;white-space:nowrap">488 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">69.557 ns</td><td style="text-align:right;white-space:nowrap">368 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">204.296 ns</td><td style="text-align:right;white-space:nowrap">752 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">5,063.661 ns</td><td style="text-align:right;white-space:nowrap">12,448 B</td></tr>
</tbody>
</table>

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```