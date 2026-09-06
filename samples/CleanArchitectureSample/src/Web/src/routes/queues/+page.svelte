<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { replaceState } from '$app/navigation';
  import { queuesApi } from '$lib/api';
  import { Button, Spinner, Alert, Sparkline } from '$lib/components/ui';
  import QueueScenarios from '$lib/components/queues/QueueScenarios.svelte';
  import JobInspector from '$lib/components/queues/JobInspector.svelte';
  import DeadLetters from '$lib/components/queues/DeadLetters.svelte';
  import {
    JOB_STATUSES,
    JOB_STATUS_COLORS,
    statusLabel,
    isTerminal,
    elapsed,
    type QueueSummary,
    type JobSummary,
    type JobDashboardView,
    type DeadLetterView,
    type HostInfoView,
    type EnqueueReceipt
  } from '$lib/types/queue';
  import { auth } from '$lib/stores/auth.svelte';
  import { eventStream } from '$lib/stores/eventstream.svelte';
  import { toast } from '$lib/stores/toast.svelte';

  let queues = $state<QueueSummary[]>([]);
  let host = $state<HostInfoView | null>(null);
  let selectedQueue = $state<string | null>(null);
  let tab = $state<'jobs' | 'dead-letters' | 'settings'>('jobs');
  let status = $state('active');
  let skip = $state(0);
  const pageSize = 25;
  let dashboard = $state<JobDashboardView | null>(null);
  let letters = $state<DeadLetterView[] | null>(null);
  let selectedJobId = $state<string | null>(null);
  let job = $state<JobSummary | null>(null);
  let jobLookup = $state('');
  let receipt = $state<EnqueueReceipt | null>(null);
  let loading = $state(true);
  let refreshing = $state(false);
  let busy = $state<string | null>(null);
  let error = $state<string | null>(null);
  let detailError = $state<string | null>(null);
  let jobError = $state<string | null>(null);
  let lastUpdated = $state<number | null>(null);
  let now = $state(Date.now());
  let polling = $state(true);
  let filter = $state('');
  let refreshInFlight: Promise<void> | null = null;
  let stopped = false;
  const isAdmin = $derived(auth.user?.role === 'Admin');
  const selected = $derived(
    queues.find((q) => q.queueName === selectedQueue) ?? null
  );
  const visibleQueues = $derived(
    queues.filter((q) =>
      `${q.queueName} ${q.group ?? ''}`
        .toLowerCase()
        .includes(filter.toLowerCase())
    )
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
    queues.every((q) => q.statisticsAvailable)
  );
  const activity = $derived(
    eventStream.events.filter((e) => e.category === 'job').slice(0, 8)
  );

  function describeError(e: unknown): string {
    const r = e as {
      problem?: {
        title?: string;
        detail?: string;
        errors?: Record<string, string[]>;
      };
      status?: number;
      message?: string;
    };
    const fields = r?.problem?.errors
      ? Object.values(r.problem.errors).flat().join(' ')
      : '';
    return (
      fields ||
      r?.problem?.detail ||
      r?.problem?.title ||
      r?.message ||
      `Request failed${r?.status ? ` (${r.status})` : ''}`
    );
  }
  function data<T>(response: { status: number; data?: T | null }): T {
    if (response.status >= 400) throw response;
    if (response.data == null)
      throw new Error('The server returned no data. Refresh to try again.');
    return response.data;
  }
  function updateUrl() {
    const url = new URL(window.location.href);
    if (selectedQueue) url.searchParams.set('queue', selectedQueue);
    else url.searchParams.delete('queue');
    if (selectedJobId) url.searchParams.set('job', selectedJobId);
    else url.searchParams.delete('job');
    replaceState(url, {});
  }
  const detailKey = () => `${selectedQueue}:${tab}:${status}:${skip}`;
  async function loadDetail(includeDeadLetters = false) {
    if (
      !selectedQueue ||
      tab === 'settings' ||
      (tab === 'dead-letters' && !includeDeadLetters)
    )
      return;
    const key = detailKey(),
      name = selectedQueue;
    try {
      if (tab === 'jobs') {
        const next = data(
          await queuesApi.getJobDashboard(name, status, skip, pageSize)
        );
        if (key === detailKey() && !stopped) dashboard = next;
      } else {
        const next = data(await queuesApi.getDeadLetters(name, 100));
        if (key === detailKey() && !stopped) letters = next;
      }
      if (key === detailKey()) detailError = null;
    } catch (e) {
      if (key === detailKey() && !stopped) detailError = describeError(e);
    }
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
  async function refresh(includeDeadLetters = false) {
    if (busy || stopped) return;
    if (refreshInFlight) {
      await refreshInFlight;
      return refresh(includeDeadLetters);
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
        loadDetail(includeDeadLetters),
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
  async function selectQueue(
    name: string,
    which?: 'jobs' | 'dead-letters' | 'settings'
  ) {
    selectedQueue = name;
    tab =
      which ??
      (queues.find((q) => q.queueName === name)?.trackProgress
        ? 'jobs'
        : 'dead-letters');
    status = 'active';
    skip = 0;
    dashboard = null;
    letters = null;
    detailError = null;
    updateUrl();
    await refresh(tab === 'dead-letters');
    // A previous selection's request may have been in flight when this queue was selected.
    if (!dashboard && tab === 'jobs') await refresh();
    await tick();
    document
      .getElementById('selected-title')
      ?.scrollIntoView({ block: 'start' });
  }
  async function switchTab(which: 'jobs' | 'dead-letters' | 'settings') {
    tab = which;
    detailError = null;
    await refresh(which === 'dead-letters');
  }
  async function changeStatus(value: string) {
    status = value;
    skip = 0;
    dashboard = null;
    await refresh();
    if (!dashboard) await refresh();
  }
  async function changePage(next: number) {
    skip = next;
    await refresh();
  }
  function openJob(id: string) {
    selectedJobId = id.trim();
    job = null;
    jobError = null;
    updateUrl();
    void loadJob();
  }
  function closeJob() {
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
    // Complete any inspection first so operator actions do not race our own dead-letter leases.
    if (refreshInFlight) await refreshInFlight;
    try {
      done(data(await action()));
    } catch (e) {
      toast.error(describeError(e));
    } finally {
      busy = null;
      await refresh(tab === 'dead-letters');
    }
  }
  function enqueue(
    label: string,
    action: () => ReturnType<typeof queuesApi.enqueueExports>
  ) {
    void run(label, action, (value) => {
      receipt = value;
      toast.success(
        `${value.count} ${label.toLowerCase()} message${value.count === 1 ? '' : 's'} accepted.`
      );
      selectedQueue = value.queueName;
      tab = value.jobIds.length ? 'jobs' : 'dead-letters';
      status = 'active';
      skip = 0;
      dashboard = null;
      letters = null;
      detailError = null;
      updateUrl();
    });
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
  function retry(messageId?: string, max = 100) {
    const name = selectedQueue!;
    void run(
      'retry',
      () => queuesApi.replayDeadLetters(name, messageId, max),
      (result) => {
        if (!result.replayed) {
          toast.info(
            'No matching available messages were replayed. Refresh and try again.'
          );
          return;
        }
        receipt = {
          queueName: result.queueName,
          count: result.replayed,
          jobIds: result.receipts.flatMap((r) => (r.jobId ? [r.jobId] : []))
        };
        toast.success(
          `${result.replayed} message${result.replayed === 1 ? '' : 's'} replayed with new job identities.`
        );
      }
    );
  }
  function flush(max: number) {
    const name = selectedQueue!;
    void run(
      'flush',
      () => queuesApi.purgeDeadLetters(name, max),
      (result) =>
        toast.success(
          `Deleted ${result.purged} dead letter${result.purged === 1 ? '' : 's'}. Job history is preserved.`
        )
    );
  }

  onMount(() => {
    const url = new URL(window.location.href);
    selectedQueue = url.searchParams.get('queue');
    selectedJobId = url.searchParams.get('job');
    void refresh().then(async () => {
      if (
        selectedQueue &&
        queues.find((q) => q.queueName === selectedQueue)?.trackProgress ===
          false
      ) {
        tab = 'dead-letters';
        detailError = null;
        await refresh(true);
      }
    });
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
  ><title>Queue operations - Clean Architecture Sample</title></svelte:head
>

<div class="space-y-6 pb-6">
  <div class="flex flex-wrap items-start justify-between gap-4">
    <div>
      <h1 class="text-2xl font-bold text-gray-900">Queue operations</h1>
      <p class="text-sm text-gray-500 mt-1">
        Follow work from acceptance to completion, across every worker process.
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
        onclick={() => refresh(tab === 'dead-letters')}>Refresh now</Button
      >
    </div>
  </div>
  {#if error}<Alert
      type="error"
      message={`Monitoring unavailable: ${error}. Previously loaded values may be stale.`}
    />{/if}
  <div class="grid grid-cols-2 lg:grid-cols-4 gap-3">
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
  {#if !statisticsAvailable}<Alert
      type="warning"
      message="Transport statistics are unavailable for one or more queues. Counts marked — are unknown; tracked job state is shown separately."
    />{/if}
  <QueueScenarios {isAdmin} busy={!!busy} {enqueue} />
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
      <div class="flex flex-wrap gap-2">
        {#each receipt.jobIds as id}<button
            class="text-xs font-mono text-blue-700 underline"
            onclick={() => openJob(id)}>Open job {id.slice(0, 10)}</button
          >{/each}
      </div>
    </div>{/if}

  {#if selected}<section
      class="rounded-lg border bg-white shadow-sm overflow-hidden"
      aria-labelledby="selected-title"
    >
      <div
        class="p-5 border-b flex flex-wrap items-center justify-between gap-4"
      >
        <div>
          <h2 id="selected-title" class="font-semibold break-all">
            {selected.queueName}
          </h2>
          <p class="text-sm text-gray-500 mt-1">{selected.description}</p>
        </div>
        <div class="flex gap-1">
          {#if selected.trackProgress}<Button
              variant={tab === 'jobs' ? 'secondary' : 'ghost'}
              onclick={() => switchTab('jobs')}>Jobs</Button
            >{/if}<Button
            variant={tab === 'dead-letters' ? 'secondary' : 'ghost'}
            onclick={() => switchTab('dead-letters')}>Dead letters</Button
          ><Button
            variant={tab === 'settings' ? 'secondary' : 'ghost'}
            onclick={() => switchTab('settings')}>Settings</Button
          >
        </div>
      </div>
      {#if detailError}<div class="p-4">
          <Alert
            type="error"
            message={`Unable to load details: ${detailError}`}
          /><Button
            variant="outline"
            size="sm"
            onclick={() => refresh(tab === 'dead-letters')}
            >Retry loading details</Button
          >
        </div>{/if}
      {#if tab === 'jobs'}
        <div class="p-4 border-b space-y-3">
          <div class="flex flex-wrap gap-2">
            <Button
              size="sm"
              variant={status === 'active' ? 'secondary' : 'ghost'}
              onclick={() => changeStatus('active')}>Active work</Button
            ><Button
              size="sm"
              variant={status === 'all' ? 'secondary' : 'ghost'}
              onclick={() => changeStatus('all')}>All jobs</Button
            >{#each JOB_STATUSES as value}<Button
                size="sm"
                variant={status === value ? 'secondary' : 'ghost'}
                onclick={() => changeStatus(value)}
                >{statusLabel(value)} ({dashboard?.counts[value] ??
                  '—'})</Button
              >{/each}
          </div>
          <form
            class="flex flex-wrap items-center gap-2"
            onsubmit={(e) => {
              e.preventDefault();
              if (jobLookup.trim()) openJob(jobLookup);
            }}
          >
            <input
              aria-label="Find job by ID"
              bind:value={jobLookup}
              placeholder="Find a job by its full ID"
              class="border rounded px-3 py-1.5 text-sm min-w-64"
            /><Button
              type="submit"
              size="sm"
              variant="outline"
              disabled={!jobLookup.trim()}>Open job</Button
            ><span class="text-xs text-gray-400"
              >Search any tracked queue, including older jobs.</span
            >
          </form>
        </div>
        {#if !dashboard && !detailError}<div class="flex justify-center py-8">
            <Spinner />
          </div>{:else if dashboard}
          {#if dashboard.jobs.length === 0}<p
              class="p-8 text-center text-gray-500"
            >
              No jobs in this view. Try another status or enqueue a scenario.
            </p>{:else}<div class="divide-y">
              {#each dashboard.jobs as entry (entry.jobId)}<div
                  class="px-5 py-4 space-y-2"
                  data-job-id={entry.jobId}
                >
                  <div
                    class="flex flex-wrap items-center justify-between gap-3"
                  >
                    <div class="flex items-center gap-2 flex-wrap">
                      <span
                        class="text-xs rounded px-2 py-1 {JOB_STATUS_COLORS[
                          entry.status
                        ]}">{statusLabel(entry.status)}</span
                      ><button
                        class="font-mono text-xs text-blue-700 underline"
                        onclick={() => openJob(entry.jobId)}
                        >{entry.jobId.slice(0, 12)}…</button
                      ><span class="text-xs text-gray-500"
                        >Attempt {entry.attempt}{#if entry.workerId}
                          · {entry.workerId}{/if}</span
                      >{#if entry.metadata?.tenant}<span
                          class="text-xs text-gray-500"
                          >Tenant {entry.metadata.tenant}</span
                        >{/if}
                    </div>
                    <div class="flex gap-2 items-center">
                      <span class="text-xs text-gray-400"
                        >Created {elapsed(entry.createdUtc, null, now)} ago</span
                      >{#if !isTerminal(entry.status) && isAdmin}<Button
                          size="sm"
                          variant="destructive"
                          disabled={!!busy || entry.cancellationRequested}
                          onclick={() => cancelJob(entry.jobId)}
                          >{entry.cancellationRequested
                            ? 'Cancellation requested'
                            : 'Cancel'}</Button
                        >{/if}
                    </div>
                  </div>
                  {#if entry.status === 'Processing' || entry.status === 'Completed'}<div
                      class="flex gap-3 items-center"
                    >
                      <progress
                        class="h-2 w-40 accent-blue-600"
                        value={entry.progress}
                        max="100"
                        aria-label="Job progress"
                      ></progress><span class="text-xs text-gray-600"
                        >{entry.progress}% · {entry.progressMessage ?? ''}</span
                      >
                    </div>{/if}{#if entry.errorMessage}<p
                      class="text-xs text-red-700 whitespace-pre-wrap break-words"
                    >
                      {entry.errorMessage}
                    </p>{/if}
                </div>{/each}
            </div>{/if}
          <div
            class="flex flex-wrap items-center justify-between gap-3 p-4 border-t text-xs text-gray-500"
          >
            <span
              >{dashboard.total
                ? `${skip + 1}–${Math.min(skip + dashboard.jobs.length, dashboard.total)} of ${dashboard.total.toLocaleString()}`
                : '0 jobs'} · shared state, newest first</span
            >
            <div class="flex gap-2">
              <Button
                size="sm"
                variant="outline"
                disabled={skip === 0 || refreshing}
                onclick={() => changePage(Math.max(0, skip - pageSize))}
                >Previous</Button
              ><Button
                size="sm"
                variant="outline"
                disabled={skip + pageSize >= dashboard.total ||
                  skip >= 1000 ||
                  refreshing}
                onclick={() => changePage(skip + pageSize)}>Next</Button
              >
            </div>
          </div>
        {/if}
      {:else if tab === 'dead-letters'}<DeadLetters
          queueName={selected.queueName}
          {letters}
          {isAdmin}
          busy={!!busy || refreshing}
          refresh={() => refresh(true)}
          {retry}
          {flush}
          {openJob}
        />
      {:else}<div class="p-5 space-y-5">
          <dl class="grid sm:grid-cols-2 lg:grid-cols-3 gap-5 text-sm">
            {#each [['Worker group', selected.group ?? 'None'], ['Concurrency / receive cap', `${selected.concurrency} / ${selected.prefetchCount}`], ['Maximum attempts', selected.maxAttempts], ['Dead letters (24h)', selected.messagesDeadLettered], ['Retry delays', selected.retryDelays ?? selected.retryPolicy], ['Lease / renewal', `${selected.visibilityTimeoutSeconds}s / ${selected.autoRenewTimeout ? 'automatic' : 'manual'}`], ['Completion', selected.autoComplete ? 'Automatic after success' : 'Handler acknowledges'], ['Worker on answering API', selected.workerRunsHere ? (selected.isRunning ? 'Running here' : 'Not running') : 'Separate worker process'], ['Subscription', selected.handlers.length > 1 ? 'Explicit shared handler group' : 'Independent handler subscription'], ['Job tracking', selected.trackProgress ? 'Shared Redis state' : 'Not enabled']] as [label, value]}<div
              >
                <dt class="text-gray-500 text-xs">{label}</dt>
                <dd class="mt-1 font-medium">{value}</dd>
              </div>{/each}
          </dl>
          <div>
            <h3 class="text-sm font-medium mb-1">Handlers</h3>
            {#each selected.handlers as handler}<p
                class="text-xs font-mono text-gray-600 break-all"
              >
                {handler}
              </p>{/each}
          </div>
          <p class="text-xs text-gray-500">
            Validation runs before enqueue and during processing. Request tenant
            context crosses message headers into the worker's fresh scope. Queue
            names inherit the shared resource prefix; the group selector uses
            logical names. Independent subscriptions have separate retry
            budgets.
          </p>
        </div>{/if}
    </section>{/if}

  <section
    class="rounded-lg border bg-white shadow-sm"
    aria-labelledby="queues-title"
  >
    <div class="flex flex-wrap items-center justify-between gap-3 p-4 border-b">
      <div>
        <h2 id="queues-title" class="font-semibold">Subscriptions</h2>
        <p class="text-xs text-gray-500 mt-1">
          Transport counts are approximate. Counters cover the last 24 hours
          across workers.
        </p>
      </div>
      <label class="text-xs text-gray-500"
        >Filter queues<input
          aria-label="Filter queues"
          bind:value={filter}
          placeholder="Queue or group"
          class="ml-2 border rounded px-2 py-1.5 text-sm"
        /></label
      >
    </div>
    {#if loading}<div class="flex justify-center py-8">
        <Spinner />
      </div>{:else if !visibleQueues.length}<p
        class="p-6 text-center text-gray-500"
      >
        {queues.length
          ? 'No queues match your filter.'
          : 'No queues are registered.'}
      </p>{:else}<div class="overflow-x-auto">
        <table class="min-w-full text-sm">
          <thead class="bg-gray-50 text-xs text-gray-500"
            ><tr
              ><th class="p-4 text-left">Queue / worker group</th><th
                class="p-4 text-left">Completed / retries (24h)</th
              ><th class="p-4 text-right">Ready</th><th class="p-4 text-right"
                >In flight</th
              ><th class="p-4 text-right">Delayed</th><th class="p-4 text-right"
                >Dead letters</th
              ></tr
            ></thead
          ><tbody class="divide-y">
            {#each visibleQueues as queue (queue.queueName)}<tr
                class={selectedQueue === queue.queueName
                  ? 'bg-blue-50'
                  : 'hover:bg-gray-50'}
                ><td class="p-4"
                  ><button
                    class="text-blue-700 font-medium text-left underline decoration-dotted"
                    onclick={() => selectQueue(queue.queueName)}
                    >{queue.queueName}</button
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
                        >{queue.messagesProcessed.toLocaleString()}</span
                      ><Sparkline
                        data={queue.counterStats?.buckets.map(
                          (b) => b.counters.processed ?? 0
                        ) ?? []}
                        color="#16a34a"
                        label="Completed"
                      />
                    </div>
                    <div>
                      <span class="font-medium text-red-700"
                        >{queue.messagesFailed.toLocaleString()}</span
                      ><Sparkline
                        data={queue.counterStats?.buckets.map(
                          (b) => b.counters.failed ?? 0
                        ) ?? []}
                        color="#dc2626"
                        label="Retries scheduled"
                      />
                    </div>
                  </div></td
                ><td class="p-4 text-right tabular-nums"
                  >{queue.statisticsAvailable ? queue.activeCount : '—'}</td
                ><td class="p-4 text-right tabular-nums text-blue-700"
                  >{queue.statisticsAvailable ? queue.inFlightCount : '—'}</td
                ><td class="p-4 text-right tabular-nums text-orange-700"
                  >{queue.statisticsAvailable ? queue.delayedCount : '—'}</td
                ><td class="p-4 text-right"
                  ><button
                    class="text-red-700 underline"
                    onclick={() => selectQueue(queue.queueName, 'dead-letters')}
                    >{queue.statisticsAvailable
                      ? queue.deadLetterCount
                      : '—'}</button
                  ></td
                ></tr
              >{/each}
          </tbody>
        </table>
      </div>{/if}
  </section>

  <section class="rounded-lg border bg-white p-5 space-y-4">
    <div class="flex flex-wrap justify-between gap-3">
      <div>
        <h2 class="font-semibold">Live worker activity</h2>
        <p class="text-xs text-gray-500 mt-1">
          Best-effort notifications from workers to this API's event feed. Job
          state above remains the source for status.
        </p>
      </div>
      <span
        class="text-xs self-center {eventStream.isConnected
          ? 'text-green-700'
          : 'text-orange-700'}"
        >{!auth.isAuthenticated
          ? 'Sign in for live events'
          : eventStream.isConnected
            ? 'Event feed connected'
            : 'Event feed reconnecting'}</span
      >
    </div>
    {#if !activity.length}<p class="text-sm text-gray-400">
        Complete a scenario to see which worker handled it. Both bank files
        should publish completion events.
      </p>{:else}<ul class="divide-y">
        {#each activity as event (event.id)}<li
            class="py-2 flex flex-wrap gap-2 justify-between text-sm"
          >
            <span
              >{event.type}
              <span class="text-xs text-gray-500"
                >{event.host ?? 'Unknown host'}</span
              ></span
            ><span class="text-xs text-gray-400"
              >{event.timestamp.toLocaleTimeString()}{#if typeof event.data.jobId === 'string'}
                · <button
                  class="text-blue-700 underline"
                  onclick={() => openJob(String(event.data.jobId))}
                  >Open job</button
                >{/if}</span
            >
          </li>{/each}
      </ul>{/if}<a class="text-xs text-blue-700 underline" href="/events"
      >Open the full event feed</a
    >
  </section>
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
