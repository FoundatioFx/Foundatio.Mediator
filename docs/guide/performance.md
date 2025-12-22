# Performance

Foundatio.Mediator aims to get as close to direct method call performance as possible while providing a full-featured mediator with excellent developer ergonomics. Through C# interceptors and source generators, we eliminate runtime reflection entirely.

## Benchmark Results

> 📊 **Last Updated:** 2025-12-22

### Commands

Fire-and-forget dispatch with no return value.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Command</code></td><td style="text-align:right;white-space:nowrap">5.536 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Command</code></td><td style="text-align:right;white-space:nowrap">9.169 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Command</code></td><td style="text-align:right;white-space:nowrap">40.458 ns</td><td style="text-align:right;white-space:nowrap">128 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Command</code></td><td style="text-align:right;white-space:nowrap">60.987 ns</td><td style="text-align:right;white-space:nowrap">200 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Command</code></td><td style="text-align:right;white-space:nowrap">171.784 ns</td><td style="text-align:right;white-space:nowrap">704 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Command</code></td><td style="text-align:right;white-space:nowrap">1,213.024 ns</td><td style="text-align:right;white-space:nowrap">4,184 B</td></tr>
</tbody>
</table>

### Queries

Request/response dispatch returning an Order object.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Query</code></td><td style="text-align:right;white-space:nowrap">28.961 ns</td><td style="text-align:right;white-space:nowrap">192 B</td></tr>
<tr><td style="width:100%"><code>Direct_QueryWithDependencies</code></td><td style="text-align:right;white-space:nowrap">33.656 ns</td><td style="text-align:right;white-space:nowrap">264 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Query</code></td><td style="text-align:right;white-space:nowrap">33.510 ns</td><td style="text-align:right;white-space:nowrap">120 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Query</code></td><td style="text-align:right;white-space:nowrap">60.191 ns</td><td style="text-align:right;white-space:nowrap">320 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Query</code></td><td style="text-align:right;white-space:nowrap">93.022 ns</td><td style="text-align:right;white-space:nowrap">464 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Query</code></td><td style="text-align:right;white-space:nowrap">257.041 ns</td><td style="text-align:right;white-space:nowrap">1,000 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Query</code></td><td style="text-align:right;white-space:nowrap">5,368.266 ns</td><td style="text-align:right;white-space:nowrap">12,488 B</td></tr>
</tbody>
</table>

### Events (Publish)

Notification dispatched to 2 handlers.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Event</code></td><td style="text-align:right;white-space:nowrap">5.689 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Publish</code></td><td style="text-align:right;white-space:nowrap">10.769 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Publish</code></td><td style="text-align:right;white-space:nowrap">101.197 ns</td><td style="text-align:right;white-space:nowrap">792 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Publish</code></td><td style="text-align:right;white-space:nowrap">110.246 ns</td><td style="text-align:right;white-space:nowrap">336 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Publish</code></td><td style="text-align:right;white-space:nowrap">1,829.026 ns</td><td style="text-align:right;white-space:nowrap">2,840 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Publish</code></td><td style="text-align:right;white-space:nowrap">2,047.094 ns</td><td style="text-align:right;white-space:nowrap">6,008 B</td></tr>
</tbody>
</table>

### Full Query (Dependencies + Middleware)

Query where handler has an injected service (IOrderService) and timing middleware (Before/Finally or IPipelineBehavior).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>MediatorNet_FullQuery</code></td><td style="text-align:right;white-space:nowrap">41.487 ns</td><td style="text-align:right;white-space:nowrap">192 B</td></tr>
<tr><td style="width:100%"><code>MediatR_FullQuery</code></td><td style="text-align:right;white-space:nowrap">139.304 ns</td><td style="text-align:right;white-space:nowrap">744 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_FullQuery</code></td><td style="text-align:right;white-space:nowrap">192.957 ns</td><td style="text-align:right;white-space:nowrap">776 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_FullQuery</code></td><td style="text-align:right;white-space:nowrap">262.386 ns</td><td style="text-align:right;white-space:nowrap">1,000 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_FullQuery</code></td><td style="text-align:right;white-space:nowrap">5,578.827 ns</td><td style="text-align:right;white-space:nowrap">12,560 B</td></tr>
</tbody>
</table>

### Cascading Messages

CreateOrder returns an Order and publishes OrderCreatedEvent to 2 handlers. Foundatio uses tuple returns for automatic cascading; other libraries publish manually.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>MediatorNet_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">45.668 ns</td><td style="text-align:right;white-space:nowrap">144 B</td></tr>
<tr><td style="width:100%"><code>MediatR_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">173.084 ns</td><td style="text-align:right;white-space:nowrap">1,168 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">116.241 ns</td><td style="text-align:right;white-space:nowrap">568 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">2,355.686 ns</td><td style="text-align:right;white-space:nowrap">4,064 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">8,922.815 ns</td><td style="text-align:right;white-space:nowrap">18,746 B</td></tr>
</tbody>
</table>

### Short-Circuit Middleware (Foundatio Only)

Middleware returns cached result; handler is never invoked. Useful for caching or authorization.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Foundatio_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">65.948 ns</td><td style="text-align:right;white-space:nowrap">368 B</td></tr>
</tbody>
</table>

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```