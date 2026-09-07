<script lang="ts">
  import { Sparkline } from '$lib/components/ui';
  import {
    JOB_STATUSES,
    statusLabel,
    formatTime,
    type QueueSummary,
    type JobDashboardView
  } from '$lib/types/queue';
  import { queueUrl } from './utils';
  import QueueActivityChart from './QueueActivityChart.svelte';

  let {
    queue,
    counts,
    updatedUtc
  }: {
    queue: QueueSummary;
    counts: JobDashboardView['counts'] | null;
    updatedUtc: string | null;
  } = $props();
  const labels: Record<string, string> = {
    processed: 'Completed',
    failed: 'Failed attempts',
    dead_lettered: 'Dead-lettered'
  };
  const colors: Record<string, string> = {
    processed: '#16a34a',
    failed: '#ea580c',
    dead_lettered: '#dc2626'
  };
  const counters = $derived(
    queue.counterStats?.totals ?? {
      processed: queue.messagesProcessed,
      failed: queue.messagesFailed,
      dead_lettered: queue.messagesDeadLettered
    }
  );
  const keys = $derived([
    ...new Set([
      'processed',
      'failed',
      'dead_lettered',
      ...Object.keys(counters),
      ...(queue.counterStats?.buckets.flatMap((b) => Object.keys(b.counters)) ??
        [])
    ])
  ]);
  const buckets = $derived(
    [...(queue.counterStats?.buckets ?? [])].sort((a, b) =>
      a.hour.localeCompare(b.hour)
    )
  );
</script>

<div class="p-5 space-y-7">
  <section aria-labelledby="activity-title" class="space-y-4">
    <div>
      <h2 id="activity-title" class="font-semibold">
        {queue.counterStats
          ? 'Activity in the last 24 hours'
          : 'Worker counters'}
      </h2>
      <p class="text-xs text-gray-500 mt-1">
        {queue.counterStats
          ? 'Hourly counters across all workers for this queue. Failed attempts include retries and terminal failures.'
          : 'Shared counter history is unavailable. These counters cover only the responding process; other workers may have additional activity.'}
      </p>
    </div>
    <div class="grid sm:grid-cols-2 xl:grid-cols-3 gap-4">
      {#each keys as key}
        <div class="border rounded-lg p-4 min-w-0">
          <h3 class="text-xs text-gray-500">
            {labels[key] ?? key.replaceAll('_', ' ')}
          </h3>
          <p class="text-2xl font-semibold tabular-nums mt-1">
            {(counters[key] ?? 0).toLocaleString()}
          </p>
          {#if buckets.length}
            <div class="mt-3" aria-label={`${labels[key] ?? key} hourly trend`}>
              <Sparkline
                data={buckets.map((b) => b.counters[key] ?? 0)}
                color={colors[key] ?? '#2563eb'}
                width={200}
                height={40}
              />
            </div>
          {/if}
        </div>
      {/each}
    </div>
  </section>

  <section aria-labelledby="job-counts-title" class="space-y-3">
    <h2 id="job-counts-title" class="font-semibold">Tracked job states</h2>
    {#if queue.trackProgress}
      <p class="text-xs text-gray-500">
        Retained job history for this queue, separate from transport depth and
        24-hour counters.
      </p>
      <div class="grid grid-cols-2 sm:grid-cols-3 xl:grid-cols-4 gap-3">
        {#each JOB_STATUSES as status}
          <a
            href={`${queueUrl(queue.queueName, 'jobs')}&status=${status}`}
            class="rounded-lg border p-3 hover:bg-blue-50 focus-visible:outline-blue-600"
          >
            <span class="block text-xs text-gray-500"
              >{statusLabel(status)}</span
            >
            <span class="block text-xl font-semibold mt-1 tabular-nums"
              >{counts ? counts[status].toLocaleString() : '—'}</span
            >
          </a>
        {/each}
      </div>
      {#if updatedUtc}<p class="text-xs text-gray-400">
          Job counts updated {formatTime(updatedUtc)}
        </p>{/if}
    {:else}
      <p class="text-sm text-gray-500">
        Job tracking is not enabled for this queue. Transport counts, processing
        counters, and dead letters are available.
      </p>
    {/if}
  </section>

  {#if queue.counterStats}
    <section aria-labelledby="hourly-title" class="space-y-3">
      <h2 id="hourly-title" class="font-semibold">Hourly statistics</h2>
      {#if buckets.length}
        <QueueActivityChart queueName={queue.queueName} {buckets} />
      {:else}<p class="text-sm text-gray-500">
          No hourly activity has been recorded in this window.
        </p>{/if}
    </section>
  {/if}
</div>
