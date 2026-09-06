<script lang="ts">
  import { Button } from '$lib/components/ui';
  import { queuesApi } from '$lib/api';
  import { tenant } from '$lib/stores/tenant.svelte';
  let {
    isAdmin,
    busy,
    enqueue
  }: {
    isAdmin: boolean;
    busy: boolean;
    enqueue: (
      label: string,
      action: () => ReturnType<typeof queuesApi.enqueueExports>
    ) => void;
  } = $props();
  let count = $state(1);
  let duration = $state(30);
  let outcome = $state('success');
  let webhookFailures = $state(3);
  let bank = $state('first-national');
  const exportJob = () =>
    enqueue('Export', () =>
      queuesApi.enqueueExports(
        count,
        20,
        duration * 50,
        outcome === 'retry' ? 1 : 0,
        outcome === 'dead-letter'
      )
    );
</script>

<section
  class="rounded-lg border border-gray-200 bg-white shadow-sm"
  aria-labelledby="scenarios-title"
>
  <div class="px-5 py-4 border-b flex flex-wrap justify-between gap-2">
    <div>
      <h2 id="scenarios-title" class="font-semibold">Try the workflow</h2>
      <p class="text-sm text-gray-500 mt-1">
        Predictable jobs to observe, cancel, retry, and inspect.
      </p>
    </div>
    <span class="text-xs text-gray-500 self-center"
      >Tenant: <strong>{tenant.current}</strong> · captured with the requesting user</span
    >
  </div>
  {#if !isAdmin}<div class="px-5 py-4 text-sm text-gray-600">
      <a href="/login?redirect=/queues" class="text-blue-600 underline"
        >Sign in</a
      >
      as <strong>admin / admin</strong> to run scenarios and manage work. Monitoring
      remains available without signing in.
    </div>{/if}
  <div
    class="grid grid-cols-1 lg:grid-cols-3 divide-y lg:divide-y-0 lg:divide-x"
  >
    <div class="p-5 space-y-3">
      <h3 class="font-medium">Export with progress</h3>
      <p class="text-xs text-gray-500">
        Watch worker identity, heartbeat, and tenant context. Cancel a queued or
        running job. Invalid duration is rejected before enqueue.
      </p>
      <div class="flex gap-3">
        <label class="text-xs text-gray-600 flex-1"
          >Jobs<input
            aria-label="Export job count"
            type="number"
            min="1"
            max="100"
            bind:value={count}
            class="mt-1 block w-full border rounded px-2 py-1.5"
          /></label
        ><label class="text-xs text-gray-600 flex-1"
          >Seconds / attempt<input
            aria-label="Export duration"
            type="number"
            min="1"
            max="100"
            bind:value={duration}
            class="mt-1 block w-full border rounded px-2 py-1.5"
          /></label
        >
      </div>
      <label class="text-xs text-gray-600 block"
        >Outcome<select
          aria-label="Export outcome"
          bind:value={outcome}
          class="mt-1 block w-full border rounded px-2 py-1.5"
          ><option value="success">Complete successfully</option><option
            value="retry">Fail once, then recover</option
          ><option value="dead-letter">Fail immediately to dead letter</option
          ></select
        ></label
      >
      <Button disabled={!isAdmin || busy} onclick={exportJob}
        >Enqueue export</Button
      >
    </div>
    <div class="p-5 space-y-3">
      <h3 class="font-medium">Retry and dead-letter recovery</h3>
      <p class="text-xs text-gray-500">
        The simulated webhook gets three attempts. Fail once to watch a retry,
        or fail three times to create a dead letter. Replaying simulates fixing
        the remote service and creates a new tracked job.
      </p>
      <label class="text-xs text-gray-600 block"
        >Initial failures<select
          aria-label="Webhook failures"
          bind:value={webhookFailures}
          class="mt-1 block w-full border rounded px-2 py-1.5"
          ><option value={1}>1 · recover on attempt 2</option><option value={3}
            >3 · move to dead letters</option
          ></select
        ></label
      >
      <Button
        disabled={!isAdmin || busy}
        onclick={() =>
          enqueue('Webhook', () =>
            queuesApi.enqueueFlakyWebhook(
              'https://hooks.example.com/orders',
              webhookFailures
            )
          )}>Enqueue webhook</Button
      >
      <p class="text-xs text-gray-400">No external HTTP request is made.</p>
    </div>
    <div class="p-5 space-y-3">
      <h3 class="font-medium">Backlog and resource locks</h3>
      <p class="text-xs text-gray-500">
        Imports run one at a time per worker. Two bank files sharing a key both
        run, sequentially while the lock is owned; neither is discarded.
      </p>
      <Button
        variant="outline"
        disabled={!isAdmin || busy}
        onclick={() =>
          enqueue('Imports', () => queuesApi.enqueueImports(3, 200, 50))}
        >Enqueue 3 imports</Button
      >
      <label class="text-xs text-gray-600 block"
        >Shared bank key<input
          aria-label="Bank key"
          bind:value={bank}
          class="mt-1 block w-full border rounded px-2 py-1.5"
        /></label
      >
      <Button
        variant="outline"
        disabled={!isAdmin || busy}
        onclick={() =>
          enqueue('Bank files', () => queuesApi.enqueueBankFiles(bank, 2))}
        >Enqueue 2 bank files</Button
      >
    </div>
  </div>
  <p class="border-t px-5 py-4 text-xs text-gray-500">
    <a href="/orders/new" class="text-blue-700 underline">Create an order</a> to
    exercise independent subscriptions and the shared confirmation/fulfillment
    queue.
    <a href="/products" class="text-blue-700 underline">Update a product</a> to see
    notifications invalidate caches across API replicas. Inspect subscriptions below
    and follow the full event feed.
  </p>
</section>
