<script lang="ts">
  import { Button, Spinner } from '$lib/components/ui';
  import {
    JOB_STATUSES,
    JOB_STATUS_COLORS,
    statusLabel,
    isTerminal,
    elapsed,
    type JobDashboardView
  } from '$lib/types/queue';
  let {
    dashboard,
    status,
    skip,
    pageSize,
    now,
    busy,
    refreshing,
    error,
    isAdmin,
    changeStatus,
    changePage,
    openJob,
    cancelJob
  }: {
    dashboard: JobDashboardView | null;
    status: string;
    skip: number;
    pageSize: number;
    now: number;
    busy: boolean;
    refreshing: boolean;
    error: string | null;
    isAdmin: boolean;
    changeStatus: (status: string) => void;
    changePage: (skip: number) => void;
    openJob: (id: string) => void;
    cancelJob: (id: string) => void;
  } = $props();
  let jobLookup = $state('');
</script>

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
        >{statusLabel(value)} ({dashboard?.counts[value] ?? '—'})</Button
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
{#if !dashboard && !error}<div class="flex justify-center py-8">
    <Spinner />
  </div>{:else if dashboard}
  {#if dashboard.jobs.length === 0}<p class="p-8 text-center text-gray-500">
      No jobs in this view. Try another status or search for a job by ID.
    </p>{:else}<div class="divide-y">
      {#each dashboard.jobs as entry (entry.jobId)}<div
          class="px-5 py-4 space-y-2"
          data-job-id={entry.jobId}
        >
          <div class="flex flex-wrap items-center justify-between gap-3">
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
              >{#if entry.metadata?.tenant}<span class="text-xs text-gray-500"
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
