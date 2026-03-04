<script lang="ts">
  import { onMount } from 'svelte';
  import { eventStream } from '$lib/stores/eventstream.svelte';
  import { Button, Badge } from '$lib/components/ui';

  type EventEntry = {
    id: number;
    timestamp: Date;
    type: string;
    category: 'order' | 'product';
    action: 'created' | 'updated' | 'deleted';
    data: Record<string, unknown>;
  };

  let events = $state<EventEntry[]>([]);
  let paused = $state(false);
  let autoScroll = $state(true);
  let maxEvents = 200;
  let nextId = 0;
  let listEl: HTMLDivElement | undefined = $state();

  function addEvent(type: string, category: EventEntry['category'], action: EventEntry['action'], data: Record<string, unknown>) {
    if (paused) return;
    const entry: EventEntry = { id: nextId++, timestamp: new Date(), type, category, action, data };
    events = [entry, ...events].slice(0, maxEvents);
  }

  function clearEvents() {
    events = [];
  }

  function formatTime(date: Date): string {
    return date.toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 });
  }

  function formatData(data: Record<string, unknown>): string {
    return Object.entries(data)
      .map(([k, v]) => `${k}: ${typeof v === 'number' ? v.toLocaleString() : v}`)
      .join(' · ');
  }

  const actionColors: Record<EventEntry['action'], string> = {
    created: 'bg-green-100 text-green-800',
    updated: 'bg-blue-100 text-blue-800',
    deleted: 'bg-red-100 text-red-800',
  };

  const categoryColors: Record<EventEntry['category'], string> = {
    order: 'bg-purple-100 text-purple-800',
    product: 'bg-amber-100 text-amber-800',
  };

  onMount(() => {
    const unsubs = [
      eventStream.onOrderCreated((e) =>
        addEvent('OrderCreated', 'order', 'created', { orderId: e.orderId, customerId: e.customerId, amount: e.amount })
      ),
      eventStream.onOrderUpdated((e) =>
        addEvent('OrderUpdated', 'order', 'updated', { orderId: e.orderId, amount: e.amount, status: e.status })
      ),
      eventStream.onOrderDeleted((e) =>
        addEvent('OrderDeleted', 'order', 'deleted', { orderId: e.orderId })
      ),
      eventStream.onProductCreated((e) =>
        addEvent('ProductCreated', 'product', 'created', { productId: e.productId, name: e.name, price: e.price })
      ),
      eventStream.onProductUpdated((e) =>
        addEvent('ProductUpdated', 'product', 'updated', { productId: e.productId, name: e.name, price: e.price, status: e.status })
      ),
      eventStream.onProductDeleted((e) =>
        addEvent('ProductDeleted', 'product', 'deleted', { productId: e.productId })
      ),
    ];

    return () => unsubs.forEach((fn) => fn());
  });
</script>

<svelte:head>
  <title>Live Events - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-4">
  <div class="flex items-center justify-between">
    <div>
      <h1 class="text-2xl font-bold text-gray-900">Live Events</h1>
      <p class="mt-1 text-sm text-gray-500">
        Real-time SSE events from the server
      </p>
    </div>
    <div class="flex items-center gap-3">
      <div class="flex items-center gap-2">
        <span class="relative flex h-3 w-3">
          {#if eventStream.isConnected}
            <span class="animate-ping absolute inline-flex h-full w-full rounded-full bg-green-400 opacity-75"></span>
            <span class="relative inline-flex rounded-full h-3 w-3 bg-green-500"></span>
          {:else}
            <span class="relative inline-flex rounded-full h-3 w-3 bg-red-500"></span>
          {/if}
        </span>
        <span class="text-sm text-gray-600">{eventStream.isConnected ? 'Connected' : 'Disconnected'}</span>
      </div>
      <span class="text-sm text-gray-400">|</span>
      <span class="text-sm text-gray-500">{events.length} event{events.length !== 1 ? 's' : ''}</span>
      <Button variant="outline" onclick={() => paused = !paused}>
        {paused ? '▶ Resume' : '⏸ Pause'}
      </Button>
      <Button variant="secondary" onclick={clearEvents}>
        Clear
      </Button>
    </div>
  </div>

  <div
    bind:this={listEl}
    class="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden"
  >
    {#if events.length === 0}
      <div class="flex flex-col items-center justify-center py-16 text-gray-400">
        <svg class="h-12 w-12 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M13 10V3L4 14h7v7l9-11h-7z" />
        </svg>
        <p class="text-lg font-medium">Waiting for events…</p>
        <p class="text-sm mt-1">Create, update, or delete orders and products to see live events here.</p>
        {#if paused}
          <p class="text-sm mt-2 text-amber-600 font-medium">Event capture is paused</p>
        {/if}
      </div>
    {:else}
      <div class="divide-y divide-gray-100 max-h-[calc(100vh-220px)] overflow-y-auto">
        {#each events as event (event.id)}
          <div class="flex items-start gap-3 px-4 py-3 hover:bg-gray-50 transition-colors animate-fade-in">
            <span class="text-xs font-mono text-gray-400 pt-0.5 whitespace-nowrap">
              {formatTime(event.timestamp)}
            </span>
            <span class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-semibold {categoryColors[event.category]}">
              {event.category}
            </span>
            <span class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-semibold {actionColors[event.action]}">
              {event.action}
            </span>
            <span class="text-sm font-medium text-gray-900">{event.type}</span>
            <span class="text-sm text-gray-500 truncate">{formatData(event.data)}</span>
          </div>
        {/each}
      </div>
    {/if}
  </div>

  {#if paused}
    <div class="text-center py-2">
      <span class="inline-flex items-center gap-1.5 text-sm text-amber-600 font-medium">
        <span class="h-2 w-2 rounded-full bg-amber-500"></span>
        Event capture paused — new events are being dropped
      </span>
    </div>
  {/if}
</div>

<style>
  @keyframes fade-in {
    from { opacity: 0; transform: translateY(-4px); }
    to { opacity: 1; transform: translateY(0); }
  }
  :global(.animate-fade-in) {
    animation: fade-in 0.3s ease-out;
  }
</style>
