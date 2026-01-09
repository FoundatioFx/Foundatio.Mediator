# Performance

Foundatio.Mediator aims to get as close to direct method call performance as possible while providing a full-featured mediator with excellent developer ergonomics. Through C# interceptors and source generators, we eliminate runtime reflection entirely.

## Benchmark Results

> 📊 **Last Updated:** 2026-01-09

### Commands

Fire-and-forget dispatch with no return value.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Command</code></td><td style="text-align:right;white-space:nowrap">0.0018 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Command</code></td><td style="text-align:right;white-space:nowrap">7.7535 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Command</code></td><td style="text-align:right;white-space:nowrap">8.7615 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_Command</code></td><td style="text-align:right;white-space:nowrap">11.5821 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Command</code></td><td style="text-align:right;white-space:nowrap">34.8951 ns</td><td style="text-align:right;white-space:nowrap">128 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Command</code></td><td style="text-align:right;white-space:nowrap">191.5925 ns</td><td style="text-align:right;white-space:nowrap">704 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Command</code></td><td style="text-align:right;white-space:nowrap">1,894.2028 ns</td><td style="text-align:right;white-space:nowrap">4,912 B</td></tr>
</tbody>
</table>

### Queries

Request/response dispatch returning an Order object.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Query</code></td><td style="text-align:right;white-space:nowrap">21.4985 ns</td><td style="text-align:right;white-space:nowrap">48 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Query</code></td><td style="text-align:right;white-space:nowrap">25.9325 ns</td><td style="text-align:right;white-space:nowrap">48 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Query</code></td><td style="text-align:right;white-space:nowrap">29.4850 ns</td><td style="text-align:right;white-space:nowrap">48 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_Query</code></td><td style="text-align:right;white-space:nowrap">31.3242 ns</td><td style="text-align:right;white-space:nowrap">48 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Query</code></td><td style="text-align:right;white-space:nowrap">54.1867 ns</td><td style="text-align:right;white-space:nowrap">248 B</td></tr>
<tr><td style="width:100%"><code>MediatR_QueryWithDependencies</code></td><td style="text-align:right;white-space:nowrap">134.4035 ns</td><td style="text-align:right;white-space:nowrap">600 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Query</code></td><td style="text-align:right;white-space:nowrap">270.7645 ns</td><td style="text-align:right;white-space:nowrap">864 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Query</code></td><td style="text-align:right;white-space:nowrap">6,860.7615 ns</td><td style="text-align:right;white-space:nowrap">13,144 B</td></tr>
</tbody>
</table>

### Events (Publish)

Notification dispatched to 2 handlers.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_Publish</code></td><td style="text-align:right;white-space:nowrap">0.0046 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_Publish</code></td><td style="text-align:right;white-space:nowrap">6.2083 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_Publish</code></td><td style="text-align:right;white-space:nowrap">29.6835 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_Publish</code></td><td style="text-align:right;white-space:nowrap">54.4492 ns</td><td style="text-align:right;white-space:nowrap">32 B</td></tr>
<tr><td style="width:100%"><code>MediatR_Publish</code></td><td style="text-align:right;white-space:nowrap">56.4874 ns</td><td style="text-align:right;white-space:nowrap">440 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_Publish</code></td><td style="text-align:right;white-space:nowrap">1,858.9299 ns</td><td style="text-align:right;white-space:nowrap">2,840 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_Publish</code></td><td style="text-align:right;white-space:nowrap">3,019.7545 ns</td><td style="text-align:right;white-space:nowrap">7,456 B</td></tr>
</tbody>
</table>

### Full Query (Dependencies + Middleware)

Query where handler has an injected service (IOrderService) and timing middleware (Before/Finally or IPipelineBehavior).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_FullQuery</code></td><td style="text-align:right;white-space:nowrap">64.3040 ns</td><td style="text-align:right;white-space:nowrap">160 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_FullQuery</code></td><td style="text-align:right;white-space:nowrap">76.7475 ns</td><td style="text-align:right;white-space:nowrap">88 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_FullQuery</code></td><td style="text-align:right;white-space:nowrap">76.9550 ns</td><td style="text-align:right;white-space:nowrap">88 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_FullQuery</code></td><td style="text-align:right;white-space:nowrap">125.0594 ns</td><td style="text-align:right;white-space:nowrap">288 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_FullQuery</code></td><td style="text-align:right;white-space:nowrap">310.9703 ns</td><td style="text-align:right;white-space:nowrap">944 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_FullQuery</code></td><td style="text-align:right;white-space:nowrap">6,984.8064 ns</td><td style="text-align:right;white-space:nowrap">13,144 B</td></tr>
</tbody>
</table>

### Cascading Messages

CreateOrder returns an Order and publishes OrderCreatedEvent to 2 handlers. Foundatio uses tuple returns for automatic cascading; other libraries publish manually.

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">27.9258 ns</td><td style="text-align:right;white-space:nowrap">144 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">37.5463 ns</td><td style="text-align:right;white-space:nowrap">72 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">76.1077 ns</td><td style="text-align:right;white-space:nowrap">72 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">84.0305 ns</td><td style="text-align:right;white-space:nowrap">104 B</td></tr>
<tr><td style="width:100%"><code>MediatR_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">117.5655 ns</td><td style="text-align:right;white-space:nowrap">744 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">2,283.9439 ns</td><td style="text-align:right;white-space:nowrap">4,056 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_CascadingMessages</code></td><td style="text-align:right;white-space:nowrap">9,479.0192 ns</td><td style="text-align:right;white-space:nowrap">20,848 B</td></tr>
</tbody>
</table>

### Short-Circuit Middleware

Middleware returns cached result; handler is never invoked. Each library uses its idiomatic short-circuit approach (IPipelineBehavior, HandlerResult.ShortCircuit, HandlerContinuation.Stop, etc.).

<table style="width:100%">
<thead>
<tr><th style="text-align:left">Method</th><th style="text-align:right;white-space:nowrap">Mean</th><th style="text-align:right;white-space:nowrap">Allocated</th></tr>
</thead>
<tbody><tr><td style="width:100%"><code>Direct_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">0.2548 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatorNet_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">8.5877 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>ImmediateHandlers_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">9.6720 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>Foundatio_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">9.7259 ns</td><td style="text-align:right;white-space:nowrap">0 B</td></tr>
<tr><td style="width:100%"><code>MediatR_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">51.7768 ns</td><td style="text-align:right;white-space:nowrap">416 B</td></tr>
<tr><td style="width:100%"><code>Wolverine_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">230.5081 ns</td><td style="text-align:right;white-space:nowrap">824 B</td></tr>
<tr><td style="width:100%"><code>MassTransit_ShortCircuit</code></td><td style="text-align:right;white-space:nowrap">6,363.0741 ns</td><td style="text-align:right;white-space:nowrap">12,224 B</td></tr>
</tbody>
</table>

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```