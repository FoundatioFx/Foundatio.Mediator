# Performance

Foundatio.Mediator aims to get as close to direct method call performance as possible while providing a full-featured mediator with excellent developer ergonomics. Through C# interceptors and source generators, we eliminate runtime reflection entirely.

## Benchmark Results

> 📊 **Last Updated:** 2026-01-16

### Commands

Fire-and-forget dispatch with no return value.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Command</code></td><td style="text-align:right;white-space:nowrap">0.0100 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Command</code></td><td style="text-align:right;white-space:nowrap">7.791 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Command</code></td><td style="text-align:right;white-space:nowrap">8.5159 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_Command</code></td><td style="text-align:right;white-space:nowrap">11.7999 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Command</code></td><td style="text-align:right;white-space:nowrap">33.2616 ns</td><td style="text-align:right;white-space:nowrap">128 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Command</code></td><td style="text-align:right;white-space:nowrap">179.3535 ns</td><td style="text-align:right;white-space:nowrap">704 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Command</code></td><td style="text-align:right;white-space:nowrap">1,803.8562 ns</td><td style="text-align:right;white-space:nowrap">4,912 B</td></tr>
</tbody>
</table>

### Queries

Request/response dispatch returning an Order object.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Query</code></td><td style="text-align:right;white-space:nowrap">21.3071 ns</td><td style="text-align:right;white-space:nowrap">48 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Query</code></td><td style="text-align:right;white-space:nowrap">26.1184 ns</td><td style="text-align:right;white-space:nowrap">48 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_Query</code></td><td style="text-align:right;white-space:nowrap">29.9819 ns</td><td style="text-align:right;white-space:nowrap">48 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Query</code></td><td style="text-align:right;white-space:nowrap">32.510 ns</td><td style="text-align:right;white-space:nowrap">48 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Query</code></td><td style="text-align:right;white-space:nowrap">52.1734 ns</td><td style="text-align:right;white-space:nowrap">248 B</td></tr>
<tr><td style="width:100%"><code>MediatR_QueryWithDependencies</code></td><td style="text-align:right;white-space:nowrap">129.0559 ns</td><td style="text-align:right;white-space:nowrap">600 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Query</code></td><td style="text-align:right;white-space:nowrap">276.8493 ns</td><td style="text-align:right;white-space:nowrap">864 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Query</code></td><td style="text-align:right;white-space:nowrap">6,273.1643 ns</td><td style="text-align:right;white-space:nowrap">13,144 B</td></tr>
</tbody>
</table>

### Events (Publish)

Notification dispatched to 2 handlers.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Publish</code></td><td style="text-align:right;white-space:nowrap">0.0075 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Publish</code></td><td style="text-align:right;white-space:nowrap">5.7770 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Publish</code></td><td style="text-align:right;white-space:nowrap">26.321 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_Publish</code></td><td style="text-align:right;white-space:nowrap">52.5714 ns</td><td style="text-align:right;white-space:nowrap">32 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Publish</code></td><td style="text-align:right;white-space:nowrap">53.2317 ns</td><td style="text-align:right;white-space:nowrap">440 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Publish</code></td><td style="text-align:right;white-space:nowrap">1,785.4066 ns</td><td style="text-align:right;white-space:nowrap">2,840 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Publish</code></td><td style="text-align:right;white-space:nowrap">3,042.1838 ns</td><td style="text-align:right;white-space:nowrap">7,456 B</td></tr>
</tbody>
</table>

### Full Query (Dependencies + Middleware)

Query where handler has an injected service (IOrderService) and timing middleware (Before/Finally or IPipelineBehavior).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_FullQuery</code></td><td style="text-align:right;white-space:nowrap">62.0578 ns</td><td style="text-align:right;white-space:nowrap">160 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_FullQuery</code></td><td style="text-align:right;white-space:nowrap">74.9773 ns</td><td style="text-align:right;white-space:nowrap">88 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_FullQuery</code></td><td style="text-align:right;white-space:nowrap">75.3614 ns</td><td style="text-align:right;white-space:nowrap">88 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_FullQuery</code></td><td style="text-align:right;white-space:nowrap">111.597 ns</td><td style="text-align:right;white-space:nowrap">288 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_FullQuery</code></td><td style="text-align:right;white-space:nowrap">289.5623 ns</td><td style="text-align:right;white-space:nowrap">944 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_FullQuery</code></td><td style="text-align:right;white-space:nowrap">6,261.0016 ns</td><td style="text-align:right;white-space:nowrap">13,144 B</td></tr>
</tbody>
</table>

### Cascading Messages

CreateOrder returns an Order and publishes OrderCreatedEvent to 2 handlers. Foundatio uses tuple returns for automatic cascading; other libraries publish manually.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">28.6472 ns</td><td style="text-align:right;white-space:nowrap">144 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">38.9334 ns</td><td style="text-align:right;white-space:nowrap">72 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">73.875 ns</td><td style="text-align:right;white-space:nowrap">72 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">89.9943 ns</td><td style="text-align:right;white-space:nowrap">104 B</td></tr>
<tr><td style="width:100%"><code>MediatR_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">122.1518 ns</td><td style="text-align:right;white-space:nowrap">744 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">2,410.2542 ns</td><td style="text-align:right;white-space:nowrap">4,056 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">11,081.7005 ns</td><td style="text-align:right;white-space:nowrap">20,848 B</td></tr>
</tbody>
</table>

### Short-Circuit Middleware

Middleware returns cached result; handler is never invoked. Each library uses its idiomatic short-circuit approach (IPipelineBehavior, HandlerResult.ShortCircuit, HandlerContinuation.Stop, etc.).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">0.0660 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">8.3342 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">8.891 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">9.1095 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">50.4434 ns</td><td style="text-align:right;white-space:nowrap">416 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">212.6274 ns</td><td style="text-align:right;white-space:nowrap">824 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">5,392.3131 ns</td><td style="text-align:right;white-space:nowrap">12,224 B</td></tr>
</tbody>
</table>

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```