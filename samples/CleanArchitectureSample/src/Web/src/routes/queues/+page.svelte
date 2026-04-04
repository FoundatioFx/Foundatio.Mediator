<script lang="ts">
  import { onMount } from 'svelte';
  import { queuesApi } from '$lib/api';
  import { Button, Badge, Spinner, Alert } from '$lib/components/ui';
  import type { QueueSummary, JobSummary, JobStatus, JobDashboardView } from '$lib/types/queue';
  import { JOB_STATUS_COLORS } from '$lib/types/queue';

  let workers = $state<QueueSummary[]>([]);
  let selectedQueue = $state<string | null>(null);
  let dashboard = $state<JobDashboardView | null>(null);
  let loading = $state(true);
  let jobsLoading = $state(false);
  let error = $state<string | null>(null);
  let demoLoading = $state(false);
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let jobPollTimer: ReturnType<typeof setInterval> | null = null;

  async function loadWorkers() {
    try {
      const result = await queuesApi.listWorkers();
      if (result.data) {
        workers = result.data;
        error = null;
      }
    } catch (e) {
      error = (e as Error).message || 'Failed to load queue workers';
    } finally {
      loading = false;
    }
  }

  async function loadJobs(queueName: string) {
    jobsLoading = true;
    try {
      const result = await queuesApi.getJobDashboard(queueName);
      if (result.data) {
        dashboard = result.data;
      }
    } catch {
      dashboard = null;
    } finally {
      jobsLoading = false;
    }
  }

  async function selectQueue(queueName: string) {
    if (selectedQueue === queueName) {
      selectedQueue = null;
      dashboard = null;
      stopJobPolling();
      return;
    }
    selectedQueue = queueName;
    await loadJobs(queueName);
    startJobPolling(queueName);
  }

  function startJobPolling(queueName: string) {
    stopJobPolling();
    jobPollTimer = setInterval(async () => {
      if (selectedQueue === queueName) {
        try {
          const result = await queuesApi.getJobDashboard(queueName);
          if (result.data) dashboard = result.data;
        } catch { /* ignore */ }
      }
    }, 1000);
  }

  function stopJobPolling() {
    if (jobPollTimer) {
      clearInterval(jobPollTimer);
      jobPollTimer = null;
    }
  }

  async function cancelJob(jobId: string) {
    try {
      await queuesApi.cancelJob(jobId);
    } catch (e) {
      console.error('Failed to cancel job:', e);
    }
  }

  async function enqueueDemoJob(count = 1) {
    demoLoading = true;
    try {
      await queuesApi.enqueueDemoJob(count, 10, 1000);
      // Refresh workers + jobs
      await loadWorkers();
      if (selectedQueue) {
        await loadJobs(selectedQueue);
      }
      // Auto-select the demo queue to show progress
      const demoQueue = workers.find((w) => w.queueName === 'DemoExportJob');
      if (demoQueue && !selectedQueue) {
        selectedQueue = 'DemoExportJob';
        await loadJobs('DemoExportJob');
        startJobPolling('DemoExportJob');
      }
    } catch (e) {
      error = (e as Error).message || 'Failed to enqueue demo job';
    } finally {
      demoLoading = false;
    }
  }

  function formatDuration(start: string | null, end: string | null): string {
    if (!start) return '—';
    const startTime = new Date(start).getTime();
    const endTime = end ? new Date(end).getTime() : Date.now();
    const ms = endTime - startTime;
    if (ms < 1000) return `${ms}ms`;
    const secs = Math.floor(ms / 1000);
    if (secs < 60) return `${secs}s`;
    return `${Math.floor(secs / 60)}m ${secs % 60}s`;
  }

  function formatTime(iso: string): string {
    return new Date(iso).toLocaleTimeString('en-US', {
      hour12: false,
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
  }

  onMount(() => {
    loadWorkers();
    pollTimer = setInterval(loadWorkers, 3000);
    return () => {
      if (pollTimer) clearInterval(pollTimer);
      stopJobPolling();
    };
  });
</script>

<svelte:head>
  <title>Queue Dashboard - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <!-- Header -->
  <div class="flex items-center justify-between">
    <div>
      <h1 class="text-2xl font-bold text-gray-900">Queue Dashboard</h1>
      <p class="mt-1 text-sm text-gray-500">Monitor queue workers, job progress, and manage running jobs.</p>
    </div>
    <div class="flex items-center gap-2">
      <Button onclick={() => enqueueDemoJob(1)} loading={demoLoading}>
        Enqueue Demo Job
      </Button>
      <Button onclick={() => enqueueDemoJob(10)} loading={demoLoading} variant="outline">
        Enqueue 10 Jobs
      </Button>
    </div>
  </div>

  {#if error}
    <Alert variant="destructive">{error}</Alert>
  {/if}

  {#if loading}
    <div class="flex justify-center py-12">
      <Spinner />
    </div>
  {:else if workers.length === 0}
    <div class="text-center py-12 text-gray-400">
      <p class="text-lg">No queue workers registered</p>
      <p class="text-sm mt-2">Queue handlers will appear here when the application starts.</p>
    </div>
  {:else}
    <!-- Workers Table -->
    <div class="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden">
      <table class="min-w-full divide-y divide-gray-200">
        <thead class="bg-gray-50">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Queue</th>
            <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
            <th class="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Processed</th>
            <th class="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Failed</th>
            <th class="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Queued</th>
            <th class="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">In Flight</th>
            <th class="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Dead Letter</th>
            <th class="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">Concurrency</th>
            <th class="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">Tracking</th>
          </tr>
        </thead>
        <tbody class="bg-white divide-y divide-gray-200">
          {#each workers as worker}
            <tr
              class="cursor-pointer transition-colors {selectedQueue === worker.queueName
                ? 'bg-blue-50'
                : 'hover:bg-gray-50'}"
              onclick={() => selectQueue(worker.queueName)}
            >
              <td class="px-4 py-3">
                <div class="text-sm font-medium text-gray-900">{worker.queueName}</div>
                <div class="text-xs text-gray-500">{worker.messageType}</div>
              </td>
              <td class="px-4 py-3">
                {#if worker.isRunning}
                  <span class="inline-flex items-center gap-1.5">
                    <span class="relative flex h-2 w-2">
                      <span class="animate-ping absolute inline-flex h-full w-full rounded-full bg-green-400 opacity-75"></span>
                      <span class="relative inline-flex rounded-full h-2 w-2 bg-green-500"></span>
                    </span>
                    <span class="text-sm text-green-700">Running</span>
                  </span>
                {:else}
                  <span class="inline-flex items-center gap-1.5">
                    <span class="inline-flex rounded-full h-2 w-2 bg-gray-400"></span>
                    <span class="text-sm text-gray-500">Stopped</span>
                  </span>
                {/if}
              </td>
              <td class="px-4 py-3 text-right text-sm text-gray-700 tabular-nums">{worker.messagesProcessed.toLocaleString()}</td>
              <td class="px-4 py-3 text-right text-sm tabular-nums {worker.messagesFailed > 0 ? 'text-red-600 font-medium' : 'text-gray-700'}">{worker.messagesFailed.toLocaleString()}</td>
              <td class="px-4 py-3 text-right text-sm text-gray-700 tabular-nums">{worker.activeCount.toLocaleString()}</td>
              <td class="px-4 py-3 text-right text-sm tabular-nums {worker.inFlightCount > 0 ? 'text-blue-600 font-medium' : 'text-gray-700'}">{worker.inFlightCount.toLocaleString()}</td>
              <td class="px-4 py-3 text-right text-sm tabular-nums {worker.deadLetterCount > 0 ? 'text-red-600 font-medium' : 'text-gray-700'}">{worker.deadLetterCount.toLocaleString()}</td>
              <td class="px-4 py-3 text-center text-sm text-gray-700">{worker.concurrency}</td>
              <td class="px-4 py-3 text-center">
                {#if worker.trackProgress}
                  <Badge text="Tracked" class="bg-blue-100 text-blue-800" />
                {:else}
                  <span class="text-xs text-gray-400">—</span>
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>

    <!-- Jobs Panel -->
    {#if selectedQueue}
      {@const queueWorker = workers.find((w) => w.queueName === selectedQueue)}
      <div class="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden">
        <div class="px-4 py-3 border-b border-gray-200 bg-gray-50 flex items-center justify-between">
          <div>
            <h2 class="text-lg font-semibold text-gray-900">{selectedQueue}</h2>
            <p class="text-xs text-gray-500 mt-0.5">
              Retry: {queueWorker?.retryPolicy} · Max retries: {queueWorker?.maxRetries}
            </p>
          </div>
          <Button variant="ghost" size="sm" onclick={() => { selectedQueue = null; dashboard = null; stopJobPolling(); }}>
            ✕
          </Button>
        </div>

        {#if !queueWorker?.trackProgress}
          <div class="px-4 py-8 text-center text-gray-400">
            <p>Progress tracking is not enabled for this queue.</p>
            <p class="text-xs mt-1">Add <code class="bg-gray-100 px-1 py-0.5 rounded">TrackProgress = true</code> to the <code class="bg-gray-100 px-1 py-0.5 rounded">[Queue]</code> attribute.</p>
          </div>
        {:else if jobsLoading && !dashboard}
          <div class="flex justify-center py-8">
            <Spinner size="sm" />
          </div>
        {:else if dashboard && (dashboard.queuedCount > 0 || dashboard.activeJobs.length > 0 || dashboard.recentJobs.length > 0)}
          <!-- Queued count banner -->
          {#if dashboard.queuedCount > 0}
            <div class="px-4 py-2 bg-gray-50 border-b border-gray-100 flex items-center gap-2">
              <span class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium bg-gray-100 text-gray-800">Queued</span>
              <span class="text-sm text-gray-600">{dashboard.queuedCount.toLocaleString()} job{dashboard.queuedCount === 1 ? '' : 's'} waiting</span>
            </div>
          {/if}

          <!-- Active (Processing) jobs -->
          {#if dashboard.activeJobs.length > 0}
            <div class="divide-y divide-gray-100">
              {#each dashboard.activeJobs as job (job.jobId)}
                <div class="px-4 py-3">
                  <div class="flex items-center justify-between mb-1">
                    <div class="flex items-center gap-2">
                      <span class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium {JOB_STATUS_COLORS[job.status] ?? 'bg-gray-100 text-gray-800'}">
                        {job.status}
                      </span>
                      <span class="text-xs text-gray-500 font-mono">{job.jobId.slice(0, 12)}…</span>
                    </div>
                    <div class="flex items-center gap-3">
                      <span class="text-xs text-gray-400">
                        {formatTime(job.createdUtc)}
                        {#if job.startedUtc}
                          · {formatDuration(job.startedUtc, job.completedUtc)}
                        {/if}
                      </span>
                      <Button variant="destructive" size="sm" onclick={() => cancelJob(job.jobId)}>
                        Cancel
                      </Button>
                    </div>
                  </div>
                  <div class="mt-2">
                    <div class="flex items-center justify-between text-xs mb-1">
                      <span class="text-gray-500">{job.progressMessage ?? ''}</span>
                      <span class="font-medium tabular-nums text-blue-600">{job.progress}%</span>
                    </div>
                    <div class="w-full bg-gray-100 rounded-full h-2 overflow-hidden">
                      <div class="h-2 rounded-full transition-all duration-500 bg-blue-500" style="width: {job.progress}%"></div>
                    </div>
                  </div>
                </div>
              {/each}
            </div>
          {/if}

          <!-- Terminal (Completed/Failed/Cancelled) jobs -->
          {#if dashboard.recentJobs.length > 0}
            {#if dashboard.activeJobs.length > 0}
              <div class="border-t border-gray-200"></div>
            {/if}
            <div class="divide-y divide-gray-100">
              {#each dashboard.recentJobs as job (job.jobId)}
                <div class="px-4 py-3 opacity-75">
                  <div class="flex items-center justify-between mb-1">
                    <div class="flex items-center gap-2">
                      <span class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium {JOB_STATUS_COLORS[job.status] ?? 'bg-gray-100 text-gray-800'}">
                        {job.status}
                      </span>
                      <span class="text-xs text-gray-500 font-mono">{job.jobId.slice(0, 12)}…</span>
                    </div>
                    <span class="text-xs text-gray-400">
                      {formatTime(job.createdUtc)}
                      {#if job.startedUtc}
                        · {formatDuration(job.startedUtc, job.completedUtc)}
                      {/if}
                    </span>
                  </div>

                  {#if job.status === 'Completed'}
                    <div class="mt-2">
                      <div class="flex items-center justify-between text-xs mb-1">
                        <span class="text-gray-500">{job.progressMessage ?? ''}</span>
                        <span class="font-medium tabular-nums text-green-600">{job.progress}%</span>
                      </div>
                      <div class="w-full bg-gray-100 rounded-full h-2 overflow-hidden">
                        <div class="h-2 rounded-full bg-green-500" style="width: {job.progress}%"></div>
                      </div>
                    </div>
                  {/if}

                  {#if job.errorMessage}
                    <div class="mt-2 text-xs text-red-600 bg-red-50 px-2 py-1 rounded">
                      {job.errorMessage}
                    </div>
                  {/if}
                </div>
              {/each}
            </div>
          {/if}
        {:else}
          <div class="px-4 py-8 text-center text-gray-400">
            No tracked jobs yet. Enqueue a message to see jobs here.
          </div>
        {/if}
      </div>
    {/if}
  {/if}
</div>
