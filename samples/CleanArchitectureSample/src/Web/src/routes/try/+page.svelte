<script lang="ts">
  import { onDestroy } from 'svelte';
  import { queuesApi } from '$lib/api';
  import { Alert, Button } from '$lib/components/ui';
  import QueueScenarios from '$lib/components/queues/QueueScenarios.svelte';
  import WorkerActivity from '$lib/components/queues/WorkerActivity.svelte';
  import { data, describeError, queueUrl } from '$lib/components/queues/utils';
  import { auth } from '$lib/stores/auth.svelte';
  import type { EnqueueReceipt } from '$lib/types/queue';

  let receipt = $state<EnqueueReceipt | null>(null);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let stopped = false;
  const isAdmin = $derived(auth.user?.role === 'Admin');
  onDestroy(() => {
    stopped = true;
  });

  async function enqueue(
    _label: string,
    action: () => ReturnType<typeof queuesApi.enqueueExports>
  ) {
    if (busy || !isAdmin) return;
    busy = true;
    error = null;
    receipt = null;
    try {
      const next = data(await action());
      if (!stopped) receipt = next;
    } catch (e) {
      if (!stopped) error = describeError(e);
    } finally {
      if (!stopped) busy = false;
    }
  }
</script>

<svelte:head><title>Try it - Clean Architecture Sample</title></svelte:head>

<div class="space-y-6 pb-6 min-w-0">
  <div class="flex flex-wrap items-start justify-between gap-4">
    <div>
      <h1 class="text-2xl font-bold text-gray-900">Try it</h1>
      <p class="text-sm text-gray-500 mt-1">
        Run sample jobs, then follow their progress in queue monitoring.
      </p>
    </div>
    <a href="/queues" class="text-sm text-blue-700 underline"
      >Open queue dashboard</a
    >
  </div>
  <QueueScenarios {isAdmin} {busy} {enqueue} />
  {#if error}<Alert type="error" message={error} />{/if}
  {#if receipt}<div
      class="rounded-lg border border-green-200 bg-green-50 px-5 py-4 space-y-2"
      role="status"
    >
      <div class="flex justify-between gap-3">
        <div>
          <strong class="text-sm text-green-900"
            >{receipt.count} accepted on {receipt.queueName}</strong
          >
          <p class="text-xs text-green-800">
            Acceptance confirms enqueueing. Open a receipt to follow completion
            or cancellation.
          </p>
        </div>
        <Button size="sm" variant="ghost" onclick={() => (receipt = null)}
          >Dismiss</Button
        >
      </div>
      <a
        class="inline-block text-sm text-blue-700 underline"
        href={queueUrl(
          receipt.queueName,
          receipt.jobIds.length ? 'jobs' : 'overview'
        )}>View queue</a
      >
      <div class="flex flex-wrap gap-2">
        {#each receipt.jobIds as id}<a
            class="text-xs font-mono text-blue-700 underline"
            href={queueUrl(receipt.queueName, 'jobs', id)}
            >Open job {id.slice(0, 10)}</a
          >{/each}
      </div>
    </div>{/if}

  <WorkerActivity />
</div>
