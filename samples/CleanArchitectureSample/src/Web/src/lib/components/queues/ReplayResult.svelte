<script lang="ts">
  import { onMount } from 'svelte';
  import { queuesApi } from '$lib/api';
  import { Button } from '$lib/components/ui';
  import { data, queueUrl } from './utils';
  import {
    isTerminal,
    statusLabel,
    JOB_STATUS_COLORS,
    type EnqueueReceipt,
    type JobSummary
  } from '$lib/types/queue';

  let {
    receipt,
    polling,
    openJob,
    dismiss
  }: {
    receipt: EnqueueReceipt;
    polling: boolean;
    openJob: (id: string) => void;
    dismiss: () => void;
  } = $props();
  // Bound both the rendered receipt and polling cost when an operator replays a large batch.
  const ids = $derived(receipt.jobIds.slice(0, 10));
  let jobs = $state<Record<string, JobSummary>>({});
  let unavailable = $state(false);
  let stopped = false;
  let pending = false;

  async function refresh() {
    if (stopped || pending) return;
    pending = true;
    let failed = false;
    try {
      await Promise.all(
        ids.map(async (id) => {
          if (jobs[id] && isTerminal(jobs[id].status)) return;
          try {
            const next = data(await queuesApi.getJob(id));
            if (!stopped) jobs[id] = next;
          } catch {
            failed = true;
          }
        })
      );
      if (!stopped) unavailable = failed;
    } finally {
      pending = false;
    }
  }

  onMount(() => {
    void refresh();
    const timer = setInterval(() => {
      if (polling) void refresh();
    }, 2500);
    return () => {
      stopped = true;
      clearInterval(timer);
    };
  });
</script>

<section
  class="rounded-lg border border-blue-200 bg-blue-50 px-5 py-4 space-y-3"
  aria-label="Retry result"
>
  <div class="flex justify-between gap-3">
    <div>
      <p class="text-sm font-medium text-blue-900" role="status">
        {receipt.count} message{receipt.count === 1 ? '' : 's'} removed from dead
        letters and returned to the normal queue.
      </p>
      <p class="text-xs text-blue-800 mt-1">
        Retrying uses the same payload. A new failure creates a new dead letter;
        previous failed-job history is preserved.
      </p>
    </div>
    <div class="flex flex-wrap items-start gap-2">
      {#if !polling && ids.length}<Button
          size="sm"
          variant="outline"
          onclick={refresh}>Check retry status</Button
        >{/if}
      <Button size="sm" variant="ghost" onclick={dismiss}>Dismiss</Button>
    </div>
  </div>
  {#if unavailable}
    <p class="text-sm text-orange-800" role="status">
      Retry job status is unavailable. The messages were requeued.
    </p>
    {#if polling}<Button size="sm" variant="outline" onclick={refresh}
        >Check retry status</Button
      >{/if}
  {/if}
  {#if ids.length}
    <ul class="space-y-2">
      {#each ids as id (id)}
        {@const job = jobs[id]}
        <li class="flex flex-wrap items-center gap-3">
          <button
            class="text-xs font-mono text-blue-700 underline"
            onclick={() => openJob(id)}>Open new job {id.slice(0, 10)}</button
          >
          <span
            class="rounded px-2 py-1 text-xs font-medium {job
              ? JOB_STATUS_COLORS[job.status]
              : 'bg-gray-100 text-gray-800'}"
            role="status"
          >
            {job?.status === 'Failed'
              ? 'Failed again'
              : job
                ? statusLabel(job.status)
                : 'Awaiting job status'}
          </span>
          {#if job?.status === 'Failed'}<span class="text-xs text-red-800"
              >{job.errorMessage ?? 'Open the new job for failure details.'} Refresh
              dead letters to inspect its new record.</span
            >{/if}
        </li>
      {/each}
    </ul>
    {#if receipt.jobIds.length > ids.length}
      <a
        class="text-xs text-blue-700 underline"
        href={queueUrl(receipt.queueName, 'jobs')}
        >Showing the first {ids.length} retry jobs. View all jobs</a
      >
    {/if}
  {/if}
</section>
