<script lang="ts">
  import { onMount } from 'svelte';
  import { page } from '$app/stores';
  import { goto } from '$app/navigation';
  import { queuesApi } from '$lib/api';
  import { Button, Spinner, Alert } from '$lib/components/ui';
  import { auth } from '$lib/stores/auth.svelte';
  import { toast } from '$lib/stores/toast.svelte';
  import {
    JOB_STATUSES,
    type QueueSummary,
    type JobSummary,
    type JobDashboardView,
    type DeadLetterView,
    type EnqueueReceipt
  } from '$lib/types/queue';
  import {
    data,
    describeError,
    queueLabel,
    queueUrl,
    QUEUE_VIEWS,
    type QueueView
  } from './utils';
  import QueueJobs from './QueueJobs.svelte';
  import QueueSettings from './QueueSettings.svelte';
  import QueueStatistics from './QueueStatistics.svelte';
  import DeadLetters from './DeadLetters.svelte';
  import JobInspector from './JobInspector.svelte';

  let { queueName }: { queueName: string } = $props();
  let queue = $state<QueueSummary | null>(null);
  let tab = $state<QueueView>('overview');
  let status = $state('active');
  let skip = $state(0);
  const pageSize = 25;
  let dashboard = $state<JobDashboardView | null>(null);
  let counts = $state<JobDashboardView['counts'] | null>(null);
  let countsUpdated = $state<string | null>(null);
  let letters = $state<DeadLetterView[] | null>(null);
  let receipt = $state<EnqueueReceipt | null>(null);
  let selectedJobId = $state<string | null>(null);
  let job = $state<JobSummary | null>(null);
  let jobError = $state<string | null>(null);
  let error = $state<string | null>(null);
  let detailError = $state<string | null>(null);
  let notFound = $state(false);
  let loading = $state(true);
  let refreshing = $state(false);
  let busy = $state(false);
  let polling = $state(true);
  let lastUpdated = $state<number | null>(null);
  let now = $state(Date.now());
  let stopped = false;
  let refreshInFlight: Promise<void> | null = null;
  const isAdmin = $derived(auth.user?.role === 'Admin');
  const detailKey = () => `${tab}:${status}:${skip}`;

  function navigate(changes: Record<string, string | null>) {
    const url = new URL($page.url);
    for (const [name, value] of Object.entries(changes)) {
      if (value === null) url.searchParams.delete(name);
      else url.searchParams.set(name, value);
    }
    void goto(url, { noScroll: true, keepFocus: true });
  }
  const openJob = (id: string) => navigate({ job: id.trim() });
  const closeJob = () => {
    if (selectedJobId && !stopped) navigate({ job: null });
  };

  async function loadJob() {
    const id = selectedJobId;
    if (!id) return;
    try {
      const next = data(await queuesApi.getJob(id));
      if (!stopped && selectedJobId === id) {
        job = next;
        jobError = null;
      }
    } catch (e) {
      if (!stopped && selectedJobId === id) jobError = describeError(e);
    }
  }
  async function loadDetail(includeDeadLetters: boolean) {
    const key = detailKey();
    try {
      if ((tab === 'overview' || tab === 'jobs') && queue?.trackProgress) {
        const next = data(
          await queuesApi.getJobDashboard(
            queueName,
            tab === 'jobs' ? status : 'active',
            tab === 'jobs' ? skip : 0,
            tab === 'jobs' ? pageSize : 1
          )
        );
        if (!stopped && key === detailKey()) {
          counts = next.counts;
          countsUpdated = next.updatedUtc;
          if (tab === 'jobs') dashboard = next;
        }
      } else if (tab === 'dead-letters') {
        // Inspection leases messages: only enter or explicitly refresh this view, never poll it.
        if (!includeDeadLetters) return;
        const next = data(await queuesApi.getDeadLetters(queueName, 100));
        if (!stopped && key === detailKey()) letters = next;
      }
      if (!stopped && key === detailKey()) detailError = null;
    } catch (e) {
      if (!stopped && key === detailKey()) detailError = describeError(e);
    }
  }
  async function refresh(includeDeadLetters = false): Promise<void> {
    if (stopped || busy) return;
    if (refreshInFlight) {
      await refreshInFlight;
      return refresh(includeDeadLetters);
    }
    refreshing = true;
    refreshInFlight = (async () => {
      await Promise.all([
        (async () => {
          try {
            const next = data(await queuesApi.get(queueName));
            if (stopped) return;
            queue = next;
            notFound = false;
            error = null;
            lastUpdated = Date.now();
            await loadDetail(includeDeadLetters);
          } catch (e) {
            if (stopped) return;
            error = describeError(e);
            notFound = (e as { status?: number }).status === 404;
            if (notFound) {
              queue = null;
              dashboard = null;
              letters = null;
            }
          }
        })(),
        loadJob()
      ]);
    })();
    try {
      await refreshInFlight;
    } finally {
      refreshInFlight = null;
      if (!stopped) {
        refreshing = false;
        loading = false;
        now = Date.now();
      }
    }
  }
  async function run<T>(
    action: () => Promise<{ status: number; data?: T | null }>,
    done: (value: T) => void
  ) {
    if (busy || stopped) return;
    busy = true;
    // Inspection must release its leases before retry or flush can select these messages.
    if (refreshInFlight) await refreshInFlight;
    try {
      if (stopped) return;
      const result = data(await action());
      if (!stopped) done(result);
    } catch (e) {
      if (!stopped) toast.error(describeError(e));
    } finally {
      busy = false;
      await refresh(tab === 'dead-letters');
    }
  }
  function cancelJob(id: string) {
    void run(
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
    void run(
      () => queuesApi.replayDeadLetters(queueName, messageId, max),
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
  function flush(max: number, messageId?: string) {
    void run(
      () => queuesApi.purgeDeadLetters(queueName, max, messageId),
      (result) => {
        if (!result.purged) {
          toast.info(
            'No matching available messages were deleted. Refresh and try again.'
          );
          return;
        }
        toast.success(
          `Deleted ${result.purged} dead letter${result.purged === 1 ? '' : 's'}. Job history is preserved.`
        );
      }
    );
  }

  onMount(() => {
    let previousKey: string | null = null;
    const unsubscribe = page.subscribe(({ url, params }) => {
      if (params.name !== queueName || stopped) return;
      const view = url.searchParams.get('view');
      tab = QUEUE_VIEWS.includes(view as QueueView)
        ? (view as QueueView)
        : 'overview';
      const requestedStatus = url.searchParams.get('status') ?? 'active';
      status = ['active', 'all', ...JOB_STATUSES].includes(requestedStatus)
        ? requestedStatus
        : 'active';
      const offset = Number(url.searchParams.get('skip'));
      skip =
        Number.isInteger(offset) && offset >= 0 && offset <= 1000 ? offset : 0;
      const id = url.searchParams.get('job');
      const jobChanged = selectedJobId !== id;
      if (jobChanged) {
        selectedJobId = id;
        job = null;
        jobError = null;
      }
      const key = detailKey();
      if (key !== previousKey) {
        previousKey = key;
        dashboard = null;
        detailError = null;
        if (tab === 'dead-letters') letters = null;
        void refresh(tab === 'dead-letters');
      } else if (jobChanged) void loadJob();
    });
    const timer = setInterval(() => {
      now = Date.now();
      if (polling && !refreshing) void refresh();
    }, 2500);
    return () => {
      stopped = true;
      unsubscribe();
      clearInterval(timer);
    };
  });
</script>

<svelte:head>
  <title
    >{queue ? queueLabel(queue) : queueName} - Queue details - Clean Architecture
    Sample</title
  >
</svelte:head>

<div class="space-y-5 pb-6 min-w-0">
  <a href="/queues" class="text-sm text-blue-700 underline">← Back to queues</a>
  <div class="flex flex-wrap items-start justify-between gap-4">
    <div class="min-w-0">
      <p class="text-xs uppercase tracking-wide text-gray-500 mb-1">
        Queue details
      </p>
      <h1 class="text-2xl font-bold text-gray-900 break-all">
        {queue ? queueLabel(queue) : queueName}
      </h1>
      {#if queue && queueLabel(queue) !== queueName}
        <p class="mt-1 text-xs font-mono text-gray-500 break-all">
          {queueName}
        </p>
      {/if}
      {#if queue}<p class="mt-2 text-sm text-gray-500">
          {queue.description ?? queue.messageType.split('.').pop()}
        </p>
        <div class="mt-3 flex flex-wrap gap-2 text-xs text-gray-600">
          <span class="rounded bg-gray-100 px-2 py-1"
            >{queue.group ?? 'No worker group'}</span
          ><span class="rounded bg-gray-100 px-2 py-1"
            >{queue.trackProgress ? 'Tracked jobs' : 'Transport only'}</span
          >
        </div>
      {/if}
    </div>
    <div class="flex flex-wrap items-center gap-2">
      <span class="text-xs text-gray-500" role="status"
        >{refreshing
          ? 'Refreshing…'
          : lastUpdated
            ? `Updated ${new Date(lastUpdated).toLocaleTimeString()}`
            : 'Connecting…'}</span
      >
      <Button size="sm" variant="outline" onclick={() => (polling = !polling)}
        >{polling ? 'Pause updates' : 'Resume updates'}</Button
      >
      <Button
        size="sm"
        variant="outline"
        disabled={refreshing || busy}
        onclick={() => refresh(tab === 'dead-letters')}>Refresh now</Button
      >
    </div>
  </div>

  {#if error}<Alert
      type="error"
      message={notFound
        ? 'Queue not found. It may have been removed or renamed.'
        : `Monitoring unavailable: ${error}. Previously loaded values may be stale.`}
    />{/if}
  {#if loading && !queue}<div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>{/if}
  {#if queue}
    <div
      class="grid grid-cols-2 lg:grid-cols-4 gap-3"
      aria-label="Queue transport statistics"
    >
      {#each [{ label: 'Ready to run', value: queue.activeCount, hint: 'Available messages', color: 'text-gray-900' }, { label: 'In flight', value: queue.inFlightCount, hint: 'Leased by workers, including lock waits', color: 'text-blue-700' }, { label: 'Delayed', value: queue.delayedCount, hint: 'Scheduled or waiting for retry', color: 'text-orange-700' }, { label: 'Dead letters', value: queue.deadLetterCount, hint: 'Failures awaiting operator action', color: 'text-red-700' }] as metric}
        <div class="rounded-lg border bg-white p-4">
          <h2 class="text-xs text-gray-500">{metric.label}</h2>
          <p class="text-3xl font-semibold tabular-nums my-2 {metric.color}">
            {queue.statisticsAvailable ? metric.value.toLocaleString() : '—'}
          </p>
          <p class="text-xs text-gray-400">{metric.hint}</p>
        </div>
      {/each}
    </div>
    <p class="text-xs text-gray-500">
      Transport counts are approximate and apply only to this queue.
    </p>
    {#if !queue.statisticsAvailable}<Alert
        type="warning"
        message="Transport statistics are unavailable. Counts marked — are unknown; tracked job state is shown separately."
      />{/if}

    {#if receipt}<div
        class="rounded-lg border border-green-200 bg-green-50 px-5 py-4 space-y-3"
        role="status"
      >
        <div class="flex justify-between gap-3">
          <p class="text-sm text-green-900">
            {receipt.count} message{receipt.count === 1 ? '' : 's'} replayed. Original
            failed job history is preserved.
          </p>
          <Button size="sm" variant="ghost" onclick={() => (receipt = null)}
            >Dismiss</Button
          >
        </div>
        <div class="flex flex-wrap gap-3">
          {#each receipt.jobIds as id}<button
              class="text-xs font-mono text-blue-700 underline"
              onclick={() => openJob(id)}>Open new job {id.slice(0, 10)}</button
            >{/each}
        </div>
      </div>{/if}

    <div class="rounded-lg border bg-white shadow-sm overflow-hidden">
      <nav class="flex flex-wrap gap-1 border-b p-3" aria-label="Queue views">
        {#each QUEUE_VIEWS as view}
          {#if view !== 'jobs' || queue.trackProgress}
            <a
              href={queueUrl(queueName, view)}
              data-sveltekit-noscroll
              aria-current={tab === view ? 'page' : undefined}
              class="rounded-md px-4 py-2 text-sm font-medium {tab === view
                ? 'bg-blue-50 text-blue-700'
                : 'text-gray-600 hover:bg-gray-100'}"
              >{view === 'overview'
                ? 'Overview'
                : view === 'jobs'
                  ? 'Jobs'
                  : view === 'dead-letters'
                    ? 'Dead letters'
                    : 'Settings'}</a
            >
          {/if}
        {/each}
      </nav>
      {#if detailError}<div class="p-4 space-y-2">
          <Alert
            type="error"
            message={`Unable to load details: ${detailError}. Previously loaded values may be stale.`}
          /><Button
            variant="outline"
            size="sm"
            disabled={refreshing || busy}
            onclick={() => refresh(tab === 'dead-letters')}
            >Retry loading details</Button
          >
        </div>{/if}
      {#if tab === 'overview'}
        <QueueStatistics {queue} {counts} updatedUtc={countsUpdated} />
      {:else if tab === 'settings'}
        <QueueSettings {queue} />
      {:else if tab === 'jobs'}
        {#if queue.trackProgress}<QueueJobs
            {dashboard}
            {status}
            {skip}
            {pageSize}
            {now}
            {busy}
            {refreshing}
            error={detailError}
            {isAdmin}
            {openJob}
            {cancelJob}
            changeStatus={(value) => navigate({ status: value, skip: null })}
            changePage={(value) =>
              navigate({ skip: value ? String(value) : null })}
          />
        {:else}<p class="p-5 text-sm text-gray-500">
            Job tracking is not enabled for this queue. Open Overview for
            transport statistics and processing counters.
          </p>{/if}
      {:else if letters !== null || !detailError}
        {#if !auth.loading && !isAdmin}
          <div class="px-5 pt-5">
            <p class="rounded-lg border bg-gray-50 p-4 text-sm text-gray-600">
              Administrator access is required to retry or purge dead letters.
              {#if auth.isAuthenticated}
                Your current account has read-only access.
              {:else}
                <a
                  class="font-medium text-blue-700 underline"
                  href={`/login?redirect=${encodeURIComponent($page.url.pathname + $page.url.search)}`}
                  >Sign in to manage dead letters</a
                >.
              {/if}
            </p>
          </div>
        {/if}
        <DeadLetters
          {queueName}
          {letters}
          {isAdmin}
          busy={busy || refreshing}
          refresh={() => refresh(true)}
          {retry}
          {flush}
          {openJob}
        />
      {/if}
    </div>
  {/if}
</div>

<JobInspector
  jobId={selectedJobId}
  {job}
  error={jobError}
  {isAdmin}
  {busy}
  {now}
  onclose={closeJob}
  oncancel={cancelJob}
  onretry={loadJob}
/>
