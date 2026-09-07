<script lang="ts">
  import { onMount } from 'svelte';
  import { goto, replaceState } from '$app/navigation';
  import { queuesApi } from '$lib/api';
  import { Button, Spinner, Alert, Sparkline } from '$lib/components/ui';
  import WorkerActivity from '$lib/components/queues/WorkerActivity.svelte';
  import JobInspector from '$lib/components/queues/JobInspector.svelte';
  import { data, describeError, queueUrl } from '$lib/components/queues/utils';
  import {
    type QueueSummary,
    type JobSummary,
    type HostInfoView
  } from '$lib/types/queue';
  import { auth } from '$lib/stores/auth.svelte';
  import { toast } from '$lib/stores/toast.svelte';

  let queues = $state<QueueSummary[]>([]);
  let host = $state<HostInfoView | null>(null);
  let selectedJobId = $state<string | null>(null);
  let job = $state<JobSummary | null>(null);
  let jobLookup = $state('');
  let loading = $state(true);
  let refreshing = $state(false);
  let busy = $state<string | null>(null);
  let error = $state<string | null>(null);
  let jobError = $state<string | null>(null);
  let lastUpdated = $state<number | null>(null);
  let now = $state(Date.now());
  let polling = $state(true);
  let filter = $state('');
  let queueView = $state('all');
  let refreshInFlight: Promise<void> | null = null;
  let stopped = false;
  const isAdmin = $derived(auth.user?.role === 'Admin');
  const visibleQueues = $derived(
    queues.filter(
      (q) =>
        `${q.queueName} ${q.group ?? ''}`
          .toLowerCase()
          .includes(filter.toLowerCase()) &&
        (queueView === 'all' ||
          (queueView === 'dead-letters' &&
            q.statisticsAvailable &&
            q.deadLetterCount > 0) ||
          (queueView === 'in-flight' &&
            q.statisticsAvailable &&
            q.inFlightCount > 0) ||
          (queueView === 'unavailable' && !q.statisticsAvailable) ||
          (queueView === 'tracked' && q.trackProgress))
    )
  );
  const deadLetterQueues = $derived(
    queues.filter((q) => q.statisticsAvailable && q.deadLetterCount > 0).length
  );
  const unavailableQueues = $derived(
    queues.filter((q) => !q.statisticsAvailable).length
  );
  const totals = $derived(
    queues.reduce(
      (sum, q) => ({
        ready: sum.ready + q.activeCount,
        delayed: sum.delayed + q.delayedCount,
        running: sum.running + q.inFlightCount,
        dead: sum.dead + q.deadLetterCount
      }),
      { ready: 0, delayed: 0, running: 0, dead: 0 }
    )
  );
  const statisticsAvailable = $derived(
    lastUpdated !== null && queues.every((q) => q.statisticsAvailable)
  );
  function updateUrl() {
    const url = new URL(window.location.href);
    if (selectedJobId) url.searchParams.set('job', selectedJobId);
    else url.searchParams.delete('job');
    replaceState(url, {});
  }
  async function loadJob() {
    const id = selectedJobId;
    if (!id) return;
    try {
      const next = data(await queuesApi.getJob(id));
      if (selectedJobId === id && !stopped) {
        job = next;
        jobError = null;
      }
    } catch (e) {
      if (selectedJobId === id && !stopped) jobError = describeError(e);
    }
  }
  async function refresh() {
    if (busy || stopped) return;
    if (refreshInFlight) {
      await refreshInFlight;
      return refresh();
    }
    refreshing = true;
    refreshInFlight = (async () => {
      await Promise.all([
        (async () => {
          try {
            const next = await Promise.all([
              queuesApi.list(),
              queuesApi.host()
            ]);
            if (!stopped) {
              queues = data(next[0]);
              host = data(next[1]);
              error = null;
              lastUpdated = Date.now();
            }
          } catch (e) {
            if (!stopped) error = describeError(e);
          }
        })(),
        loadJob()
      ]);
    })();
    try {
      await refreshInFlight;
    } finally {
      refreshInFlight = null;
      refreshing = false;
      loading = false;
      now = Date.now();
    }
  }
  function openJob(id: string) {
    selectedJobId = id.trim();
    job = null;
    jobError = null;
    updateUrl();
    void loadJob();
  }
  function closeJob() {
    if (stopped) return;
    selectedJobId = null;
    job = null;
    jobError = null;
    updateUrl();
  }

  async function run<T>(
    name: string,
    action: () => Promise<{ status: number; data?: T | null }>,
    done: (value: T) => void
  ) {
    if (busy) return;
    busy = name;
    if (refreshInFlight) await refreshInFlight;
    try {
      done(data(await action()));
    } catch (e) {
      toast.error(describeError(e));
    } finally {
      busy = null;
      await refresh();
    }
  }
  function cancelJob(id: string) {
    void run(
      'cancel',
      () => queuesApi.cancelJob(id),
      (result) => {
        if (result.cancellationRequested) {
          if (job?.jobId === id) job = { ...job, cancellationRequested: true };
          toast.info(
            'Cancellation requested. Waiting for worker confirmation.'
          );
        }
      }
    );
  }
  onMount(() => {
    const url = new URL(window.location.href);
    const legacyQueue = url.searchParams.get('queue');
    if (legacyQueue) {
      void goto(
        queueUrl(
          legacyQueue,
          url.searchParams.has('job') ? 'jobs' : 'overview',
          url.searchParams.get('job') ?? undefined
        ),
        { replaceState: true }
      );
      return;
    }
    selectedJobId = url.searchParams.get('job');
    void refresh();
    const timer = setInterval(() => {
      now = Date.now();
      if (polling && !refreshing) void refresh();
    }, 2500);
    return () => {
      stopped = true;
      clearInterval(timer);
    };
  });
</script>

<svelte:head
  ><title>Queue dashboard - Clean Architecture Sample</title></svelte:head
>

<div class="space-y-6 pb-6">
  <div class="flex flex-wrap items-start justify-between gap-4">
    <div>
      <h1 class="text-2xl font-bold text-gray-900">Queue dashboard</h1>
      <p class="text-sm text-gray-500 mt-1">
        Backlog, processing activity, and failures across all queues.
      </p>
    </div>
    <div class="flex flex-wrap items-center gap-2">
      <span class="text-xs text-gray-500" role="status"
        >{refreshing
          ? 'Refreshing…'
          : lastUpdated
            ? `Updated ${new Date(lastUpdated).toLocaleTimeString()}`
            : 'Connecting…'}</span
      ><Button size="sm" variant="outline" onclick={() => (polling = !polling)}
        >{polling ? 'Pause updates' : 'Resume updates'}</Button
      ><Button
        size="sm"
        variant="outline"
        disabled={refreshing || !!busy}
        onclick={() => refresh()}>Refresh now</Button
      >
    </div>
  </div>
  {#if error}<Alert
      type="error"
      message={`Monitoring unavailable: ${error}. Previously loaded values may be stale.`}
    />{/if}
  <div
    class="grid grid-cols-2 lg:grid-cols-4 gap-3"
    role="group"
    aria-label="Queue transport totals"
  >
    {#each [{ label: 'Ready to run', value: totals.ready, hint: 'Available transport messages', color: 'text-gray-900' }, { label: 'In flight', value: totals.running, hint: 'Leased by workers, including lock waits', color: 'text-blue-700' }, { label: 'Delayed', value: totals.delayed, hint: 'Scheduled or waiting for retry', color: 'text-orange-700' }, { label: 'Dead letters', value: totals.dead, hint: 'Failures awaiting operator action', color: 'text-red-700' }] as metric}
      <div class="rounded-lg border bg-white p-4">
        <div class="text-xs text-gray-500">{metric.label}</div>
        <div class="text-3xl font-semibold tabular-nums my-2 {metric.color}">
          {loading || !statisticsAvailable
            ? '—'
            : metric.value.toLocaleString()}
        </div>
        <div class="text-xs text-gray-400">{metric.hint}</div>
      </div>
    {/each}
  </div>
  {#if lastUpdated !== null && !statisticsAvailable}<Alert
      type="warning"
      message="Transport statistics are unavailable for one or more queues. Counts marked — are unknown; tracked job state is shown separately."
    />{/if}

  <form
    class="flex flex-wrap gap-2 items-center"
    onsubmit={(event) => {
      event.preventDefault();
      if (jobLookup.trim()) openJob(jobLookup);
    }}
  >
    <input
      aria-label="Find job by ID"
      bind:value={jobLookup}
      placeholder="Find a job by its full ID"
      class="border rounded px-3 py-2 text-sm w-full sm:w-80"
    />
    <Button type="submit" variant="outline" disabled={!jobLookup.trim()}
      >Open job</Button
    >
  </form>

  <section
    class="rounded-lg border bg-white shadow-sm"
    aria-labelledby="queues-title"
  >
    <div class="flex flex-wrap items-center justify-between gap-3 p-4 border-b">
      <div>
        <h2 id="queues-title" class="font-semibold">Subscriptions</h2>
        <p class="text-xs text-gray-500 mt-1">
          {lastUpdated === null ? '—' : queues.length} registered · {lastUpdated ===
          null
            ? '—'
            : deadLetterQueues} with dead letters · {lastUpdated === null
            ? '—'
            : unavailableQueues} with unavailable statistics
        </p>
      </div>
      <div class="flex flex-wrap gap-3">
        <label class="text-xs text-gray-500"
          >Search
          <input
            aria-label="Filter queues"
            bind:value={filter}
            placeholder="Queue or group"
            class="mt-1 block w-full border rounded px-2 py-1.5 text-sm"
          />
        </label>
        <label class="text-xs text-gray-500"
          >Show
          <select
            aria-label="Queue view"
            bind:value={queueView}
            class="mt-1 block border rounded px-2 py-1.5 text-sm"
          >
            <option value="all">All queues</option>
            <option value="dead-letters">With dead letters</option>
            <option value="in-flight">In flight</option>
            <option value="unavailable">Unavailable statistics</option>
            <option value="tracked">Tracked jobs</option>
          </select>
        </label>
      </div>
    </div>
    {#if loading}<div class="flex justify-center py-8">
        <Spinner />
      </div>{:else if !visibleQueues.length}<p
        class="p-6 text-center text-gray-500"
      >
        {lastUpdated === null
          ? 'Queue data is unavailable. Use Refresh now to try again.'
          : queues.length
            ? 'No queues match your filter.'
            : 'No queues are registered.'}
      </p>{:else}<div class="overflow-x-auto">
        <table class="min-w-full text-sm" aria-label="Queue subscriptions">
          <thead class="bg-gray-50 text-xs text-gray-500"
            ><tr
              ><th class="p-4 text-left">Queue / worker group</th><th
                class="p-4 text-left">Completed / failed attempts (24h)</th
              ><th class="p-4 text-right">Ready</th><th class="p-4 text-right"
                >In flight</th
              ><th class="p-4 text-right">Delayed</th><th class="p-4 text-right"
                >Dead letters</th
              ></tr
            ></thead
          ><tbody class="divide-y">
            {#each visibleQueues as queue (queue.queueName)}<tr
                class="hover:bg-gray-50"
                ><td class="p-4"
                  ><a
                    class="text-blue-700 font-medium text-left underline decoration-dotted"
                    href={queueUrl(queue.queueName)}>{queue.queueName}</a
                  >
                  <div class="text-xs text-gray-500 mt-1">
                    {queue.group ?? 'No group'} · {queue.trackProgress
                      ? 'Tracked jobs'
                      : 'Transport only'} · concurrency {queue.concurrency}
                  </div></td
                ><td class="p-4"
                  ><div class="flex items-center gap-4">
                    <div>
                      <span class="font-medium text-green-700"
                        >{queue.counterStats
                          ? (
                              queue.counterStats.totals.processed ?? 0
                            ).toLocaleString()
                          : '—'}</span
                      >{#if queue.counterStats}<div>
                          <Sparkline
                            data={queue.counterStats?.buckets.map(
                              (b) => b.counters.processed ?? 0
                            ) ?? []}
                            color="#16a34a"
                          />
                        </div>{/if}
                    </div>
                    <div>
                      <span class="font-medium text-red-700"
                        >{queue.counterStats
                          ? (
                              queue.counterStats.totals.failed ?? 0
                            ).toLocaleString()
                          : '—'}</span
                      >{#if queue.counterStats}<div>
                          <Sparkline
                            data={queue.counterStats?.buckets.map(
                              (b) => b.counters.failed ?? 0
                            ) ?? []}
                            color="#dc2626"
                          />
                        </div>{/if}
                    </div>
                  </div></td
                ><td class="p-4 text-right tabular-nums"
                  >{queue.statisticsAvailable ? queue.activeCount : '—'}</td
                ><td class="p-4 text-right tabular-nums text-blue-700"
                  >{queue.statisticsAvailable ? queue.inFlightCount : '—'}</td
                ><td class="p-4 text-right tabular-nums text-orange-700"
                  >{queue.statisticsAvailable ? queue.delayedCount : '—'}</td
                ><td class="p-4 text-right"
                  ><a
                    class="text-red-700 underline"
                    aria-label={`View dead letters for ${queue.queueName}`}
                    href={queueUrl(queue.queueName, 'dead-letters')}
                    >{queue.statisticsAvailable
                      ? queue.deadLetterCount
                      : '—'}</a
                  ></td
                ></tr
              >{/each}
          </tbody>
        </table>
      </div>{/if}
  </section>

  <p class="text-xs text-gray-500">
    Transport counts are approximate. Activity counters cover the last 24 hours
    across workers; — means unavailable.
  </p>
  <WorkerActivity />
  {#if host}<p class="text-xs text-gray-400">
      Last responding API: {host.hostId}. Worker selection on that node: {host.workers}.
      Separate worker processes are identified on tracked attempts.
    </p>{/if}
</div>

<JobInspector
  jobId={selectedJobId}
  {job}
  error={jobError}
  {isAdmin}
  busy={!!busy}
  {now}
  onclose={closeJob}
  oncancel={cancelJob}
  onretry={loadJob}
/>
