<script lang="ts">
  import { onMount } from 'svelte';
  import { queuesApi } from '$lib/api';
  import { Button, Spinner, Alert, Sparkline } from '$lib/components/ui';
  import type { QueueSummary, JobSummary, JobDashboardView, CounterStats, DeadLetterView, HostInfoView, EnqueueReceipt } from '$lib/types/queue';
  import { JOB_STATUS_COLORS } from '$lib/types/queue';
  import { auth } from '$lib/stores/auth.svelte';
  import { tenant } from '$lib/stores/tenant.svelte';
  import { eventStream } from '$lib/stores/eventstream.svelte';
  import { toast } from '$lib/stores/toast.svelte';

  let queues = $state<QueueSummary[]>([]);
  let host = $state<HostInfoView | null>(null);
  let selectedQueue = $state<string | null>(null);
  let tab = $state<'jobs' | 'dead-letters'>('jobs');
  let dashboard = $state<JobDashboardView | null>(null);
  let deadLetters = $state<DeadLetterView[] | null>(null);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let busy = $state<string | null>(null);

  let loadCount = $state(20);
  let webhookUrl = $state('https://hooks.example.com/orders');
  let webhookFailTimes = $state(5);
  let bank = $state('first-national');

  const isAdmin = $derived(auth.user?.role === 'Admin');
  const selected = $derived(queues.find((q) => q.queueName === selectedQueue) ?? null);

  // DemoJobCompleted arrives from the workers over the bus; tallying it by host shows how work spread across replicas.
  const completionsByHost = $derived.by(() => {
    const counts = new Map<string, number>();
    for (const e of eventStream.events) {
      if (e.type === 'DemoJobCompleted' && e.host) counts.set(e.host, (counts.get(e.host) ?? 0) + 1);
    }
    return [...counts.entries()].sort((a, b) => b[1] - a[1]);
  });

  async function loadQueues() {
    try {
      const [list, hostInfo] = await Promise.all([queuesApi.list(), queuesApi.host()]);
      if (list.data) queues = list.data;
      if (hostInfo.data) host = hostInfo.data;
      error = null;
    } catch (e) {
      error = describeError(e);
    } finally {
      loading = false;
    }
  }

  async function loadDetail() {
    if (!selectedQueue) return;
    const name = selectedQueue;
    try {
      if (tab === 'jobs') {
        const result = await queuesApi.getJobDashboard(name);
        if (selectedQueue === name && result.data) dashboard = result.data;
      } else {
        const result = await queuesApi.getDeadLetters(name);
        if (selectedQueue === name && result.data) deadLetters = result.data;
      }
    } catch (e) {
      console.warn('Failed to load queue detail:', e);
    }
  }

  function selectQueue(name: string, which: 'jobs' | 'dead-letters' | null = null) {
    if (selectedQueue === name && which === null) {
      selectedQueue = null;
      dashboard = null;
      deadLetters = null;
      return;
    }
    const queue = queues.find((q) => q.queueName === name);
    selectedQueue = name;
    tab = which ?? (queue?.trackProgress ? 'jobs' : 'dead-letters');
    dashboard = null;
    deadLetters = null;
    loadDetail();
  }

  function switchTab(which: 'jobs' | 'dead-letters') {
    tab = which;
    loadDetail();
  }

  async function run<T>(name: string, action: () => Promise<{ data?: T | null }>, onDone: (result: T) => void) {
    busy = name;
    try {
      const result = await action();
      if (result.data) onDone(result.data);
    } catch (e) {
      toast.error(describeError(e));
    } finally {
      busy = null;
      await loadQueues();
      await loadDetail();
    }
  }

  function enqueued(label: string) {
    return (receipt: EnqueueReceipt) => {
      toast.success(`${label}: ${receipt.count} message${receipt.count === 1 ? '' : 's'} on ${receipt.queueName} as tenant ${tenant.current}`);
      selectQueue(receipt.queueName, receipt.jobIds.length > 0 ? 'jobs' : 'dead-letters');
    };
  }

  const enqueueExports = (count: number) => run('exports', () => queuesApi.enqueueExports(count, 20, 1500), enqueued('Exports'));
  const enqueueImports = () => run('imports', () => queuesApi.enqueueImports(1, 200, 50), enqueued('Import'));
  const enqueueWebhook = () => run('webhook', () => queuesApi.enqueueFlakyWebhook(webhookUrl, webhookFailTimes), enqueued('Webhook'));
  const enqueueBankFiles = () => run('bank', () => queuesApi.enqueueBankFiles(bank, 2), (r) => {
    toast.success(`2 bank files for ${bank} on ${r.queueName}; watch Live Events for exactly one BankFileGenerated`);
    selectQueue(r.queueName, 'dead-letters');
  });

  const cancelJob = (jobId: string) => run('cancel', () => queuesApi.cancelJob(jobId), () => toast.info('Cancellation requested; the worker sees it on its next progress report'));
  const replayOne = (messageId: string) => run('replay', () => queuesApi.replayDeadLetters(selectedQueue!, messageId), (r) => toast.success(`Replayed ${r.replayed} message to ${r.queueName}`));
  const replayAll = () => run('replay', () => queuesApi.replayDeadLetters(selectedQueue!), (r) => toast.success(`Replayed ${r.replayed} message${r.replayed === 1 ? '' : 's'} to ${r.queueName}`));
  function purge() {
    if (!confirm(`Permanently delete every dead letter on ${selectedQueue}?`)) return;
    run('purge', () => queuesApi.purgeDeadLetters(selectedQueue!), (r) => toast.warning(`Purged ${r.purged} dead letter${r.purged === 1 ? '' : 's'} from ${r.queueName}`));
  }

  function describeError(e: unknown): string {
    const r = e as { problem?: { title?: string; detail?: string }; status?: number; message?: string };
    return r?.problem?.detail ?? r?.problem?.title ?? r?.message ?? (r?.status ? `Request failed (${r.status})` : 'Request failed');
  }

  function formatDuration(start: string | null, end: string | null): string {
    if (!start) return '—';
    const ms = (end ? new Date(end).getTime() : Date.now()) - new Date(start).getTime();
    if (ms < 1000) return `${ms}ms`;
    const secs = Math.floor(ms / 1000);
    return secs < 60 ? `${secs}s` : `${Math.floor(secs / 60)}m ${secs % 60}s`;
  }

  function formatTime(iso: string | null): string {
    return iso ? new Date(iso).toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '—';
  }

  function shortType(fullName: string | null): string {
    return fullName?.split('.').pop() ?? '—';
  }

  function bucketValues(stats: CounterStats | null | undefined, key: string): number[] {
    return stats?.buckets?.map((b) => b.counters[key] ?? 0) ?? [];
  }

  onMount(() => {
    loadQueues();
    const queuesTimer = setInterval(loadQueues, 3000);
    const detailTimer = setInterval(loadDetail, 2000);
    return () => {
      clearInterval(queuesTimer);
      clearInterval(detailTimer);
    };
  });
</script>

<svelte:head>
  <title>Queues - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex items-start justify-between gap-4">
    <div>
      <h1 class="text-2xl font-bold text-gray-900">Queues</h1>
      <p class="mt-1 text-sm text-gray-500">Workers, tracked jobs, dead letters, and the controls that exercise them.</p>
    </div>
    {#if host}
      <div class="text-right text-xs text-gray-500" title="Each poll may be answered by a different API replica">
        <div>Answered by <span class="font-mono text-gray-800">{host.hostId}</span></div>
        <div>Workers on that node: <span class="font-mono text-gray-800">{host.workers}</span></div>
      </div>
    {/if}
  </div>

  {#if error}
    <Alert type="error" message={error} />
  {/if}

  <!-- Scenario controls -->
  <div class="bg-white border border-gray-200 rounded-lg shadow-sm">
    <div class="px-4 py-3 border-b border-gray-200 bg-gray-50 flex items-center justify-between">
      <h2 class="text-sm font-semibold text-gray-900">Scenario controls</h2>
      <span class="text-xs text-gray-500">Requests carry <code class="bg-gray-100 px-1 rounded">X-Tenant: {tenant.current}</code></span>
    </div>
    {#if !isAdmin}
      <div class="px-4 py-6 text-sm text-gray-500 text-center">Sign in as <strong>admin</strong> / admin to enqueue work. Reads on this page are anonymous.</div>
    {:else}
      <div class="divide-y divide-gray-100">
        <div class="px-4 py-3 grid grid-cols-1 lg:grid-cols-[14rem_1fr_auto] gap-3 items-center">
          <div>
            <div class="text-sm font-medium text-gray-900">Tracked export jobs</div>
            <div class="text-xs text-gray-500">exports group · progress, heartbeat, cancel</div>
          </div>
          <div class="text-xs text-gray-500">One job, or a batch to watch the spread across worker replicas below.</div>
          <div class="flex items-center gap-2">
            <Button size="sm" onclick={() => enqueueExports(1)} loading={busy === 'exports'}>Enqueue 1</Button>
            <input type="number" min="1" max="100" bind:value={loadCount} class="h-8 w-20 rounded-md border border-gray-300 px-2 text-sm" />
            <Button size="sm" variant="outline" onclick={() => enqueueExports(loadCount)} loading={busy === 'exports'}>Enqueue {loadCount}</Button>
          </div>
        </div>
        <div class="px-4 py-3 grid grid-cols-1 lg:grid-cols-[14rem_1fr_auto] gap-3 items-center">
          <div>
            <div class="text-sm font-medium text-gray-900">Catalog import</div>
            <div class="text-xs text-gray-500">imports group · concurrency 1</div>
          </div>
          <div class="text-xs text-gray-500">Its own worker group; several imports queue up behind each other.</div>
          <div class="flex items-center gap-2">
            <Button size="sm" onclick={enqueueImports} loading={busy === 'imports'}>Enqueue import</Button>
          </div>
        </div>
        <div class="px-4 py-3 grid grid-cols-1 lg:grid-cols-[14rem_1fr_auto] gap-3 items-center">
          <div>
            <div class="text-sm font-medium text-gray-900">Flaky webhook</div>
            <div class="text-xs text-gray-500">events group · MaxAttempts 3 · RetryDelays 1s,3s</div>
          </div>
          <div class="text-xs text-gray-500">Fails the first N attempts. N ≥ 3 dead-letters; N = 1 recovers on retry; a bad URL dead-letters at once.</div>
          <div class="flex items-center gap-2">
            <input type="text" bind:value={webhookUrl} class="h-8 w-56 rounded-md border border-gray-300 px-2 text-sm font-mono" title="Webhook URL" />
            <input type="number" min="0" max="10" bind:value={webhookFailTimes} class="h-8 w-16 rounded-md border border-gray-300 px-2 text-sm" title="Attempts that fail" />
            <Button size="sm" onclick={enqueueWebhook} loading={busy === 'webhook'}>Enqueue</Button>
          </div>
        </div>
        <div class="px-4 py-3 grid grid-cols-1 lg:grid-cols-[14rem_1fr_auto] gap-3 items-center">
          <div>
            <div class="text-sm font-medium text-gray-900">Bank file · [QueueLock]</div>
            <div class="text-xs text-gray-500">exports group · lock key bank-file:&lt;bank&gt;</div>
          </div>
          <div class="text-xs text-gray-500">Two files for one bank land together; the lock lets one run and the other completes without running (see the worker log).</div>
          <div class="flex items-center gap-2">
            <input type="text" bind:value={bank} class="h-8 w-40 rounded-md border border-gray-300 px-2 text-sm font-mono" title="Bank" />
            <Button size="sm" onclick={enqueueBankFiles} loading={busy === 'bank'}>Enqueue 2</Button>
          </div>
        </div>
      </div>
    {/if}
  </div>

  {#if loading}
    <div class="flex justify-center py-12"><Spinner /></div>
  {:else if queues.length === 0}
    <div class="text-center py-12 text-gray-400">
      <p class="text-lg">No queues registered</p>
      <p class="text-sm mt-2">[Queue] handlers appear here once the application starts.</p>
    </div>
  {:else}
    <div class="grid grid-cols-1 xl:grid-cols-[1fr_16rem] gap-6 items-start">
      <!-- Queues table -->
      <div class="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden select-none">
        <table class="min-w-full divide-y divide-gray-200">
          <thead class="bg-gray-50">
            <tr>
              <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Queue</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Group</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Throughput (24h)</th>
              <th class="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Queued</th>
              <th class="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">In flight</th>
              <th class="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Dead letters</th>
            </tr>
          </thead>
          <tbody class="bg-white divide-y divide-gray-200">
            {#each queues as queue (queue.queueName)}
              <tr
                class="cursor-pointer transition-colors {selectedQueue === queue.queueName ? 'bg-blue-50' : 'hover:bg-gray-50'}"
                onclick={() => selectQueue(queue.queueName)}
              >
                <td class="px-4 py-2">
                  <div class="flex items-center gap-2">
                    <span class="text-sm font-medium text-gray-900">{queue.queueName}</span>
                    {#if queue.trackProgress}
                      <span class="inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-medium bg-indigo-50 text-indigo-700" title="TrackProgress = true">tracked</span>
                    {/if}
                    {#if queue.workerRunsHere}
                      <span class="inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-medium {queue.isRunning ? 'bg-green-50 text-green-700' : 'bg-yellow-50 text-yellow-700'}" title="The node that answered this request runs a worker for this queue">worker here</span>
                    {/if}
                  </div>
                  <div class="text-xs text-gray-500 mt-0.5" title={queue.description ?? ''}>
                    {shortType(queue.messageType)} · {queue.handlers.length} handler{queue.handlers.length === 1 ? '' : 's'} ({queue.handlers.map(shortType).join(', ')}) · concurrency {queue.concurrency} · {queue.maxAttempts} attempts, {queue.retryPolicy} · timeout {queue.visibilityTimeoutSeconds}s
                  </div>
                </td>
                <td class="px-4 py-2 text-sm text-gray-700"><span class="font-mono text-xs bg-gray-100 px-1.5 py-0.5 rounded">{queue.group ?? '—'}</span></td>
                <td class="px-4 py-2">
                  <div class="flex items-center gap-3">
                    <Sparkline data={bucketValues(queue.counterStats, 'processed')} color="#22c55e" label="processed" />
                    <Sparkline data={bucketValues(queue.counterStats, 'failed')} color="#ef4444" label="failed" />
                    <Sparkline data={bucketValues(queue.counterStats, 'dead_lettered')} color="#f97316" label="dead-lettered" />
                  </div>
                </td>
                <td class="px-4 py-2 text-right text-sm text-gray-700 tabular-nums">{queue.activeCount.toLocaleString()}</td>
                <td class="px-4 py-2 text-right text-sm tabular-nums {queue.inFlightCount > 0 ? 'text-blue-600 font-medium' : 'text-gray-700'}">{queue.inFlightCount.toLocaleString()}</td>
                <td class="px-4 py-2 text-right text-sm tabular-nums">
                  <button
                    type="button"
                    class="{queue.deadLetterCount > 0 ? 'text-red-600 font-medium underline decoration-dotted' : 'text-gray-700'}"
                    title="Open the dead-letter list"
                    onclick={(e) => { e.stopPropagation(); selectQueue(queue.queueName, 'dead-letters'); }}
                  >{queue.deadLetterCount.toLocaleString()}</button>
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>

      <!-- Where the work ran -->
      <div class="bg-white border border-gray-200 rounded-lg shadow-sm">
        <div class="px-4 py-3 border-b border-gray-200 bg-gray-50">
          <h2 class="text-sm font-semibold text-gray-900">Completions by host</h2>
          <p class="text-xs text-gray-500 mt-0.5">From DemoJobCompleted events on the live feed</p>
        </div>
        {#if completionsByHost.length === 0}
          <div class="px-4 py-6 text-xs text-gray-400 text-center">Enqueue a batch of exports to see which replicas pick them up.</div>
        {:else}
          <ul class="divide-y divide-gray-100">
            {#each completionsByHost as [hostId, count] (hostId)}
              <li class="px-4 py-2 flex items-center justify-between text-sm">
                <span class="font-mono text-xs text-gray-700">{hostId}</span>
                <span class="tabular-nums font-medium text-gray-900">{count}</span>
              </li>
            {/each}
          </ul>
        {/if}
      </div>
    </div>

    <!-- Detail panel -->
    {#if selected}
      <div class="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden">
        <div class="px-4 py-3 border-b border-gray-200 bg-gray-50 flex items-center justify-between gap-4">
          <div>
            <h2 class="text-lg font-semibold text-gray-900">{selected.queueName}</h2>
            <p class="text-xs text-gray-500 mt-0.5">{selected.description ?? shortType(selected.messageType)}</p>
          </div>
          <div class="flex items-center gap-1">
            {#if selected.trackProgress}
              <Button variant={tab === 'jobs' ? 'secondary' : 'ghost'} size="sm" onclick={() => switchTab('jobs')}>Jobs</Button>
            {/if}
            <Button variant={tab === 'dead-letters' ? 'secondary' : 'ghost'} size="sm" onclick={() => switchTab('dead-letters')}>
              Dead letters{#if selected.deadLetterCount > 0}&nbsp;({selected.deadLetterCount}){/if}
            </Button>
            <Button variant="ghost" size="sm" onclick={() => selectQueue(selected.queueName)}>✕</Button>
          </div>
        </div>

        {#if tab === 'jobs'}
          {#if !dashboard}
            <div class="flex justify-center py-8"><Spinner size="sm" /></div>
          {:else if dashboard.queuedCount === 0 && dashboard.activeJobs.length === 0 && dashboard.recentJobs.length === 0}
            <div class="px-4 py-8 text-center text-gray-400">No tracked jobs yet. Enqueue one above.</div>
          {:else}
            {#if dashboard.queuedCount > 0}
              <div class="px-4 py-2 bg-gray-50 border-b border-gray-100 flex items-center gap-2">
                <span class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium bg-gray-100 text-gray-800">Queued</span>
                <span class="text-sm text-gray-600">{dashboard.queuedCount.toLocaleString()} job{dashboard.queuedCount === 1 ? '' : 's'} waiting</span>
              </div>
            {/if}
            <div class="divide-y divide-gray-100">
              {#each dashboard.activeJobs as job (job.jobId)}
                {@render jobRow(job, true)}
              {/each}
              {#each dashboard.recentJobs as job (job.jobId)}
                {@render jobRow(job, false)}
              {/each}
            </div>
          {/if}
        {:else}
          <div class="px-4 py-2 border-b border-gray-100 flex items-center justify-between gap-3 text-xs text-gray-500">
            <span>Peeked from <span class="font-mono">{selected.queueName}-dead-letter</span>; replay sends a message back to its original queue from attempt 1.</span>
            {#if isAdmin && deadLetters && deadLetters.length > 0}
              <div class="flex items-center gap-2">
                <Button size="sm" variant="outline" onclick={replayAll} loading={busy === 'replay'}>Replay all</Button>
                <Button size="sm" variant="destructive" onclick={purge} loading={busy === 'purge'}>Purge</Button>
              </div>
            {/if}
          </div>
          {#if !deadLetters}
            <div class="flex justify-center py-8"><Spinner size="sm" /></div>
          {:else if deadLetters.length === 0}
            <div class="px-4 py-8 text-center text-gray-400">No dead letters on this queue.</div>
          {:else}
            <div class="divide-y divide-gray-100">
              {#each deadLetters as letter (letter.messageId)}
                <div class="px-4 py-3">
                  <div class="flex items-start justify-between gap-4">
                    <div class="min-w-0">
                      <div class="flex items-center gap-2 flex-wrap">
                        <span class="text-sm font-medium text-gray-900">{shortType(letter.messageType)}</span>
                        {#if letter.attempts !== null}
                          <span class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium bg-orange-100 text-orange-800">{letter.attempts} attempt{letter.attempts === 1 ? '' : 's'}</span>
                        {/if}
                        <span class="text-xs text-gray-400">{formatTime(letter.deadLetteredAt)}</span>
                        {#if letter.correlationId}
                          <span class="text-xs text-gray-400 font-mono" title="Correlation id (trace id of the enqueuing request)">corr {letter.correlationId.slice(0, 12)}…</span>
                        {/if}
                        {#if letter.jobId}
                          <span class="text-xs text-gray-400 font-mono" title="Tracked job id">job {letter.jobId.slice(0, 12)}…</span>
                        {/if}
                      </div>
                      {#if letter.reason}
                        <div class="mt-1 text-xs text-red-700 bg-red-50 px-2 py-1 rounded inline-block">{letter.reason}</div>
                      {/if}
                      <pre class="mt-2 text-xs text-gray-600 bg-gray-50 rounded px-2 py-1 overflow-x-auto max-h-24">{letter.body}</pre>
                    </div>
                    {#if isAdmin}
                      <Button size="sm" variant="outline" onclick={() => replayOne(letter.messageId)} loading={busy === 'replay'}>Replay</Button>
                    {/if}
                  </div>
                </div>
              {/each}
            </div>
          {/if}
        {/if}
      </div>
    {/if}
  {/if}
</div>

{#snippet jobRow(job: JobSummary, active: boolean)}
  <div class="px-4 py-3 {active ? '' : 'opacity-75'} overflow-hidden">
    <div class="flex items-center justify-between mb-1 gap-3">
      <div class="flex items-center gap-2 flex-wrap">
        <span class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium {JOB_STATUS_COLORS[job.status] ?? 'bg-gray-100 text-gray-800'}">{job.status}</span>
        {#if job.attempt > 1}
          <span class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium bg-orange-100 text-orange-800" title="This message was retried">Retry #{job.attempt - 1}</span>
        {/if}
        <span class="text-xs text-gray-500 font-mono">{job.jobId.slice(0, 12)}…</span>
        {#if job.metadata}
          {#each Object.entries(job.metadata) as [key, value] (key)}
            <span class="inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-mono bg-gray-100 text-gray-700" title="Job metadata captured at enqueue time">{key}={value}</span>
          {/each}
        {/if}
      </div>
      <div class="flex items-center gap-3 shrink-0">
        <span class="text-xs text-gray-400" title={job.lastHeartbeatUtc ? `Last heartbeat ${formatTime(job.lastHeartbeatUtc)}` : ''}>
          {formatTime(job.createdUtc)}
          {#if job.startedUtc}· {formatDuration(job.startedUtc, job.completedUtc)}{/if}
        </span>
        {#if active && isAdmin}
          <Button variant="destructive" size="sm" onclick={() => cancelJob(job.jobId)}>Cancel</Button>
        {/if}
      </div>
    </div>
    {#if active || job.status === 'Completed'}
      <div class="mt-2">
        <div class="flex items-center justify-between text-xs mb-1">
          <span class="text-gray-500">{job.progressMessage ?? ''}</span>
          <span class="font-medium tabular-nums {active ? 'text-blue-600' : 'text-green-600'}">{job.progress}%</span>
        </div>
        <div class="w-full bg-gray-100 rounded-full h-2 overflow-hidden">
          <div class="h-2 rounded-full transition-all duration-500 {active ? 'bg-blue-500' : 'bg-green-500'}" style="width: {job.progress}%"></div>
        </div>
      </div>
    {/if}
    {#if job.errorMessage}
      <div class="mt-2 text-xs text-red-600 bg-red-50 px-2 py-1 rounded truncate max-w-full" title={job.errorMessage}>{job.errorMessage}</div>
    {/if}
  </div>
{/snippet}
