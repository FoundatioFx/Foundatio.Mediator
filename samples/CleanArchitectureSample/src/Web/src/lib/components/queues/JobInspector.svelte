<script lang="ts">
  import { Button, Spinner, Alert } from '$lib/components/ui';
  import { queueUrl } from './utils';
  import {
    JOB_STATUS_COLORS,
    statusLabel,
    isTerminal,
    elapsed,
    formatTime,
    type JobSummary
  } from '$lib/types/queue';
  let {
    jobId,
    job,
    error,
    isAdmin,
    busy,
    now,
    onclose,
    oncancel,
    onretry
  }: {
    jobId: string | null;
    job: JobSummary | null;
    error: string | null;
    isAdmin: boolean;
    busy: boolean;
    now: number;
    onclose: () => void;
    oncancel: (id: string) => void;
    onretry: () => void;
  } = $props();
  let dialog: HTMLDialogElement;
  $effect(() => {
    if (jobId && dialog && !dialog.open) dialog.showModal();
    else if (!jobId && dialog?.open) dialog.close();
  });
  const stale = $derived(
    job?.status === 'Processing' &&
      now -
        new Date(
          job.lastHeartbeatUtc ?? job.startedUtc ?? job.createdUtc
        ).getTime() >
        90000
  );
</script>

<dialog
  bind:this={dialog}
  {onclose}
  class="m-auto w-[calc(100%-2rem)] max-w-2xl max-h-[90vh] overflow-y-auto rounded-xl bg-white p-0 shadow-xl backdrop:bg-black/50"
  aria-labelledby="job-detail-title"
>
  <div
    class="sticky top-0 bg-white border-b px-6 py-4 flex items-center justify-between gap-4"
  >
    <h2 id="job-detail-title" class="text-lg font-semibold">Job details</h2>
    <Button variant="ghost" size="sm" onclick={onclose}>Close</Button>
  </div>
  <div class="p-6 space-y-5">
    <div class="break-all font-mono text-xs text-gray-500">{jobId}</div>
    {#if error}<Alert type="error" message={error} /><Button
        variant="outline"
        onclick={onretry}>Retry loading job</Button
      >{/if}
    {#if !job && !error}<div class="flex justify-center py-6">
        <Spinner />
      </div>{/if}
    {#if job}
      <div class="flex flex-wrap items-center justify-between gap-3">
        <span
          class="rounded px-2 py-1 text-sm font-medium {JOB_STATUS_COLORS[
            job.status
          ]}">{statusLabel(job.status)}</span
        >
        {#if isAdmin && !isTerminal(job.status)}
          <Button
            variant="destructive"
            disabled={busy || job.cancellationRequested}
            onclick={() => oncancel(job.jobId)}
            >{job.cancellationRequested
              ? 'Cancellation requested'
              : 'Cancel job'}</Button
          >
        {/if}
      </div>
      {#if job.cancellationRequested}<Alert
          type="info"
          message="Cancellation has been requested. The worker checks before execution and during processing. Side effects already performed cannot be undone."
        />{/if}
      {#if job.status === 'EnqueueUnknown'}<Alert
          type="warning"
          message="Transport acceptance was not confirmed. Check the original operation and job state before submitting again; the message may still run."
        />{/if}
      {#if stale}<Alert
          type="warning"
          message="No heartbeat for over 90 seconds. The worker may be unavailable or the state store may be delayed; this does not prove the job stopped."
        />{/if}
      <dl class="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
        <div>
          <dt class="text-gray-500">Queue</dt>
          <dd class="break-all font-medium">
            <a
              class="text-blue-700 underline"
              href={queueUrl(job.queueName, 'jobs', job.jobId)}
              >{job.queueName}</a
            >
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Worker / latest attempt</dt>
          <dd class="break-all font-mono text-xs mt-1">
            {job.workerId ?? 'Not picked up yet'} · attempt {job.attempt}
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Created</dt>
          <dd>{formatTime(job.createdUtc)}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Attempt started</dt>
          <dd>{formatTime(job.startedUtc)}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Last heartbeat</dt>
          <dd>
            {formatTime(
              job.lastHeartbeatUtc
            )}{#if job.lastHeartbeatUtc && !isTerminal(job.status)}
              ({elapsed(job.lastHeartbeatUtc, null, now)} ago){/if}
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Last state update</dt>
          <dd>{formatTime(job.lastUpdatedUtc)}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Completed</dt>
          <dd>{formatTime(job.completedUtc)}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Attempt duration</dt>
          <dd>
            {elapsed(
              job.startedUtc,
              job.completedUtc ??
                (job.status === 'RetryPending' ? job.lastUpdatedUtc : null),
              now
            )}
          </dd>
        </div>
      </dl>
      <div>
        <div class="flex justify-between text-sm mb-2">
          <span>{job.progressMessage ?? 'Progress'}</span><span
            >{job.progress}%</span
          >
        </div>
        <progress
          class="w-full h-2 accent-blue-600"
          value={job.progress}
          max="100"
          aria-label="Job progress"
        ></progress>
      </div>
      {#if job.errorMessage}<div>
          <h3 class="text-sm font-semibold mb-2">Last failure</h3>
          <pre
            class="whitespace-pre-wrap break-words bg-red-50 text-red-800 text-xs rounded p-3">{job.errorMessage}</pre>
        </div>{/if}
      {#if job.metadata && Object.keys(job.metadata).length}<div>
          <h3 class="text-sm font-semibold mb-2">
            Context captured at enqueue
          </h3>
          <dl class="space-y-2 text-sm">
            {#each Object.entries(job.metadata) as [key, value]}<div
                class="flex gap-3"
              >
                <dt class="text-gray-500 min-w-20">{key}</dt>
                <dd class="font-mono text-xs break-all">
                  {value}
                </dd>
              </div>{/each}
          </dl>
        </div>{/if}
      <p class="text-xs text-gray-500">
        This URL opens this job directly. Job state is shared across the API and
        worker processes; the live event feed is best effort.
      </p>
    {/if}
  </div>
</dialog>
