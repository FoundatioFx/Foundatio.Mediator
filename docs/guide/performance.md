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
<tbody><tr><td style="width:100%"><code>Direct_Command</code></td><td style="text-align:right;white-space:nowrap">8.599 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Command</code></td><td style="text-align:right;white-space:nowrap">9.224 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Command</code></td><td style="text-align:right;white-space:nowrap">9.318 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Command</code></td><td style="text-align:right;white-space:nowrap">39.591 ns</td><td style="text-align:right;white-space:nowrap">128 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Command</code></td><td style="text-align:right;white-space:nowrap">181.728 ns</td><td style="text-align:right;white-space:nowrap">704 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Command</code></td><td style="text-align:right;white-space:nowrap">1,336.097 ns</td><td style="text-align:right;white-space:nowrap">4,192 B</td></tr>
</tbody>
</table>

### Queries

Request/response dispatch returning an Order object.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Query</code></td><td style="text-align:right;white-space:nowrap">29.721 ns</td><td style="text-align:right;white-space:nowrap">192 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Query</code></td><td style="text-align:right;white-space:nowrap">33.436 ns</td><td style="text-align:right;white-space:nowrap">192 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Query</code></td><td style="text-align:right;white-space:nowrap">40.132 ns</td><td style="text-align:right;white-space:nowrap">120 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Query</code></td><td style="text-align:right;white-space:nowrap">61.191 ns</td><td style="text-align:right;white-space:nowrap">320 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Query</code></td><td style="text-align:right;white-space:nowrap">256.153 ns</td><td style="text-align:right;white-space:nowrap">1,000 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Query</code></td><td style="text-align:right;white-space:nowrap">4,996.456 ns</td><td style="text-align:right;white-space:nowrap">12,496 B</td></tr>
</tbody>
</table>

### Events (Publish)

Notification dispatched to 2 handlers.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Publish</code></td><td style="text-align:right;white-space:nowrap">5.666 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Publish</code></td><td style="text-align:right;white-space:nowrap">14.334 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Publish</code></td><td style="text-align:right;white-space:nowrap">79.492 ns</td><td style="text-align:right;white-space:nowrap">136 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Publish</code></td><td style="text-align:right;white-space:nowrap">143.312 ns</td><td style="text-align:right;white-space:nowrap">792 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Publish</code></td><td style="text-align:right;white-space:nowrap">2,378.671 ns</td><td style="text-align:right;white-space:nowrap">2,840 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Publish</code></td><td style="text-align:right;white-space:nowrap">2,666.069 ns</td><td style="text-align:right;white-space:nowrap">6,016 B</td></tr>
</tbody>
</table>

### Full Query (Dependencies + Middleware)

Query where handler has an injected service (IOrderService) and timing middleware (Before/Finally or IPipelineBehavior).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>MediatorNet_FullQuery</code></td><td style="text-align:right;white-space:nowrap">54.428 ns</td><td style="text-align:right;white-space:nowrap">192 B</td></tr>
<tr><td style="width:100%"><code>Direct_FullQuery</code></td><td style="text-align:right;white-space:nowrap">83.171 ns</td><td style="text-align:right;white-space:nowrap">304 B</td></tr>
<tr><td style="width:100%"><code>MediatR_FullQuery</code></td><td style="text-align:right;white-space:nowrap">177.555 ns</td><td style="text-align:right;white-space:nowrap">744 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_FullQuery</code></td><td style="text-align:right;white-space:nowrap">262.099 ns</td><td style="text-align:right;white-space:nowrap">776 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_FullQuery</code></td><td style="text-align:right;white-space:nowrap">360.516 ns</td><td style="text-align:right;white-space:nowrap">1,000 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_FullQuery</code></td><td style="text-align:right;white-space:nowrap">5,875.182 ns</td><td style="text-align:right;white-space:nowrap">12,568 B</td></tr>
</tbody>
</table>

### Cascading Messages

CreateOrder returns an Order and publishes OrderCreatedEvent to 2 handlers. Foundatio uses tuple returns for automatic cascading; other libraries publish manually.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">52.272 ns</td><td style="text-align:right;white-space:nowrap">224 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">59.224 ns</td><td style="text-align:right;white-space:nowrap">144 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">157.596 ns</td><td style="text-align:right;white-space:nowrap">568 B</td></tr>
<tr><td style="width:100%"><code>MediatR_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">239.493 ns</td><td style="text-align:right;white-space:nowrap">1,168 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">3,018.814 ns</td><td style="text-align:right;white-space:nowrap">4,064 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">10,722.022 ns</td><td style="text-align:right;white-space:nowrap">18,778 B</td></tr>
</tbody>
</table>

### Short-Circuit Middleware

Middleware returns cached result; handler is never invoked. Each library uses its idiomatic short-circuit approach (IPipelineBehavior, HandlerResult.ShortCircuit, HandlerContinuation.Stop, etc.).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">4.099 ns</td><td style="text-align:right;white-space:nowrap">72 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">19.182 ns</td><td style="text-align:right;white-space:nowrap">72 B</td></tr>
<tr><td style="width:100%"><code>MediatR_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">80.598 ns</td><td style="text-align:right;white-space:nowrap">488 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">90.878 ns</td><td style="text-align:right;white-space:nowrap">368 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">275.620 ns</td><td style="text-align:right;white-space:nowrap">752 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">5,836.228 ns</td><td style="text-align:right;white-space:nowrap">12,448 B</td></tr>
</tbody>
</table>

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```