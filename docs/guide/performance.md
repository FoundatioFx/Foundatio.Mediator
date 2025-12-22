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
<tbody><tr><td style="width:100%"><code>Direct_Command</code></td><td style="text-align:right;white-space:nowrap">14.26 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Command</code></td><td style="text-align:right;white-space:nowrap">16.93 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Command</code></td><td style="text-align:right;white-space:nowrap">107.29 ns</td><td style="text-align:right;white-space:nowrap">128 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Command</code></td><td style="text-align:right;white-space:nowrap">179.66 ns</td><td style="text-align:right;white-space:nowrap">200 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Command</code></td><td style="text-align:right;white-space:nowrap">446.19 ns</td><td style="text-align:right;white-space:nowrap">704 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Command</code></td><td style="text-align:right;white-space:nowrap">3,141.96 ns</td><td style="text-align:right;white-space:nowrap">4,192 B</td></tr>
</tbody>
</table>

### Queries

Request/response dispatch returning an Order object.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Query</code></td><td style="text-align:right;white-space:nowrap">60.69 ns</td><td style="text-align:right;white-space:nowrap">192 B</td></tr>
<tr><td style="width:100%"><code>Direct_QueryWithDependencies</code></td><td style="text-align:right;white-space:nowrap">81.13 ns</td><td style="text-align:right;white-space:nowrap">264 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Query</code></td><td style="text-align:right;white-space:nowrap">59.71 ns</td><td style="text-align:right;white-space:nowrap">120 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Query</code></td><td style="text-align:right;white-space:nowrap">149.85 ns</td><td style="text-align:right;white-space:nowrap">320 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Query</code></td><td style="text-align:right;white-space:nowrap">210.93 ns</td><td style="text-align:right;white-space:nowrap">464 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Query</code></td><td style="text-align:right;white-space:nowrap">629.87 ns</td><td style="text-align:right;white-space:nowrap">1,000 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Query</code></td><td style="text-align:right;white-space:nowrap">12,209.45 ns</td><td style="text-align:right;white-space:nowrap">12,497 B</td></tr>
</tbody>
</table>

### Events (Publish)

Notification dispatched to 2 handlers.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Event</code></td><td style="text-align:right;white-space:nowrap">13.69 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Publish</code></td><td style="text-align:right;white-space:nowrap">22.49 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Publish</code></td><td style="text-align:right;white-space:nowrap">293.65 ns</td><td style="text-align:right;white-space:nowrap">792 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Publish</code></td><td style="text-align:right;white-space:nowrap">255.28 ns</td><td style="text-align:right;white-space:nowrap">336 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Publish</code></td><td style="text-align:right;white-space:nowrap">2,075.57 ns</td><td style="text-align:right;white-space:nowrap">1,688 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Publish</code></td><td style="text-align:right;white-space:nowrap">5,322.32 ns</td><td style="text-align:right;white-space:nowrap">6,016 B</td></tr>
</tbody>
</table>

### Full Query (Dependencies + Middleware)

Query where handler has an injected service (IOrderService) and timing middleware (Before/Finally or IPipelineBehavior).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>MediatorNet_FullQuery</code></td><td style="text-align:right;white-space:nowrap">85.38 ns</td><td style="text-align:right;white-space:nowrap">192 B</td></tr>
<tr><td style="width:100%"><code>MediatR_FullQuery</code></td><td style="text-align:right;white-space:nowrap">346.49 ns</td><td style="text-align:right;white-space:nowrap">744 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_FullQuery</code></td><td style="text-align:right;white-space:nowrap">499.30 ns</td><td style="text-align:right;white-space:nowrap">776 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_FullQuery</code></td><td style="text-align:right;white-space:nowrap">736.30 ns</td><td style="text-align:right;white-space:nowrap">1,000 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_FullQuery</code></td><td style="text-align:right;white-space:nowrap">12,693.35 ns</td><td style="text-align:right;white-space:nowrap">12,568 B</td></tr>
</tbody>
</table>

### Cascading Messages

CreateOrder returns an Order and publishes OrderCreatedEvent to 2 handlers. Foundatio uses tuple returns for automatic cascading; other libraries publish manually.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>MediatorNet_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">84.79 ns</td><td style="text-align:right;white-space:nowrap">144 B</td></tr>
<tr><td style="width:100%"><code>MediatR_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">470.51 ns</td><td style="text-align:right;white-space:nowrap">1,168 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">287.66 ns</td><td style="text-align:right;white-space:nowrap">568 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">3,403.77 ns</td><td style="text-align:right;white-space:nowrap">2,912 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">40,410.72 ns</td><td style="text-align:right;white-space:nowrap">18,824 B</td></tr>
</tbody>
</table>

### Short-Circuit Middleware

Middleware returns cached result; handler is never invoked. Each library uses its idiomatic short-circuit approach (IPipelineBehavior, HandlerResult.ShortCircuit, HandlerContinuation.Stop, etc.).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>MediatorNet_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">40.94 ns</td><td style="text-align:right;white-space:nowrap">72 B</td></tr>
<tr><td style="width:100%"><code>MediatR_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">161.28 ns</td><td style="text-align:right;white-space:nowrap">488 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">163.92 ns</td><td style="text-align:right;white-space:nowrap">368 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">585.35 ns</td><td style="text-align:right;white-space:nowrap">752 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">12,013.64 ns</td><td style="text-align:right;white-space:nowrap">12,449 B</td></tr>
</tbody>
</table>

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```