<script lang="ts">
  import { Button, Spinner } from '$lib/components/ui';
  import { formatTime, type DeadLetterView } from '$lib/types/queue';
  let {
    queueName,
    letters,
    isAdmin,
    busy,
    refresh,
    retry,
    flush,
    openJob
  }: {
    queueName: string;
    letters: DeadLetterView[] | null;
    isAdmin: boolean;
    busy: boolean;
    refresh: () => void;
    retry: (messageId?: string, max?: number) => void;
    flush: (max: number, messageId?: string) => void;
    openJob: (id: string) => void;
  } = $props();
  let inspected = $state<string | null>(null);
  let flushDialog: HTMLDialogElement;
  let flushTarget = $state<string | null>(null);
  let limit = $state(100);
  function confirmFlush(messageId: string | null = null) {
    flushTarget = messageId;
    flushDialog.showModal();
  }
  function formatted(body: string) {
    try {
      return JSON.stringify(JSON.parse(body), null, 2);
    } catch {
      return body;
    }
  }
  $effect(() => {
    queueName;
    inspected = null;
    flushTarget = null;
    flushDialog?.close();
  });
</script>

<div class="p-5 space-y-4">
  <div class="flex flex-wrap items-center justify-between gap-3">
    <p class="text-xs text-gray-500 max-w-lg">
      Showing up to 100 available messages. Refresh to check for new dead
      letters. Retrying moves a message back to the normal queue with the same
      payload. A new failure creates a new dead letter.
    </p>
    <div class="flex flex-wrap gap-2 items-center">
      <Button size="sm" variant="outline" disabled={busy} onclick={refresh}
        >Refresh dead letters</Button
      >
      {#if isAdmin}
        <label class="text-xs text-gray-500"
          >Batch limit<select
            aria-label="Dead-letter batch limit"
            bind:value={limit}
            class="ml-2 border rounded px-2 py-1.5"
            ><option value={100}>100</option><option value={1000}>1,000</option
            ><option value={10000}>10,000</option></select
          ></label
        >
        <Button
          size="sm"
          variant="outline"
          disabled={busy || !letters?.length}
          onclick={() => retry(undefined, limit)}>Retry available</Button
        >
        <Button
          size="sm"
          variant="destructive"
          disabled={busy || !letters?.length}
          onclick={() => confirmFlush()}>Flush dead letters</Button
        >
      {/if}
    </div>
  </div>
  {#if letters === null}<div class="flex justify-center py-6"><Spinner /></div>
  {:else if letters.length === 0}<div class="text-center text-gray-500 py-8">
      No available dead letters. If a worker just failed, refresh after the
      transport updates its counts.
    </div>
  {:else}
    <div class="divide-y border rounded-lg">
      {#each letters as letter (letter.messageId)}
        <div class="p-4 space-y-2" data-message-id={letter.messageId}>
          <div class="flex flex-wrap items-center justify-between gap-3">
            <div>
              <button
                class="text-blue-700 text-sm font-medium underline decoration-dotted"
                onclick={() =>
                  (inspected =
                    inspected === letter.messageId ? null : letter.messageId)}
                >Inspect {letter.messageType?.split('.').pop() ??
                  'message'}</button
              >
              <p class="text-xs text-gray-500 mt-1">
                {letter.attempts ?? 'Unknown'} attempts · {formatTime(
                  letter.deadLetteredAt
                )}
              </p>
            </div>
            <div class="flex flex-wrap gap-2">
              {#if letter.jobId}<Button
                  size="sm"
                  variant="ghost"
                  onclick={() => openJob(letter.jobId!)}
                  >{letter.replayedAt
                    ? 'Failed job'
                    : 'Original job'}</Button
                >{/if}<Button
                size="sm"
                variant="outline"
                disabled={busy || !isAdmin}
                onclick={() => retry(letter.messageId, 10000)}
                >Retry message</Button
              ><Button
                size="sm"
                variant="destructive"
                disabled={busy || !isAdmin}
                onclick={() => confirmFlush(letter.messageId)}
                >Purge message</Button
              >
            </div>
          </div>
          {#if letter.replayedAt}
            <div
              class="flex flex-wrap items-center gap-2 text-xs text-orange-800"
            >
              <span class="rounded bg-orange-50 px-2 py-1 font-medium"
                >Failed again after retry</span
              >
              <span
                >Requeued {formatTime(letter.replayedAt)}</span
              >
              {#if letter.originalJobId}
                <Button
                  size="sm"
                  variant="ghost"
                  onclick={() => { if (letter.originalJobId) openJob(letter.originalJobId); }}
                  >Previous job</Button
                >
              {/if}
            </div>
          {/if}
          <p class="text-sm text-red-700 whitespace-pre-wrap break-words">
            {letter.reason ?? 'No failure reason recorded'}
          </p>
          <p class="font-mono text-xs text-gray-400 break-all">
            {letter.messageId}
          </p>
          {#if inspected === letter.messageId}
            <div class="bg-gray-50 rounded p-3 space-y-3">
              <div>
                <h4 class="text-xs font-semibold mb-2">
                  Payload{letter.bodyTruncated ? ' (truncated preview)' : ''}
                </h4>
                <pre
                  class="text-xs whitespace-pre-wrap break-all max-h-64 overflow-y-auto">{formatted(
                    letter.body
                  )}</pre>
              </div>
              <div>
                <h4 class="text-xs font-semibold mb-2">
                  Headers and replay lineage
                </h4>
                <dl class="text-xs space-y-1">
                  {#each Object.entries(letter.headers) as [key, value]}<div
                      class="grid sm:grid-cols-[12rem_1fr] gap-1"
                    >
                      <dt class="text-gray-500 break-all">{key}</dt>
                      <dd class="font-mono break-all">{value}</dd>
                    </div>{/each}
                </dl>
              </div>
            </div>
          {/if}
        </div>
      {/each}
    </div>
  {/if}
</div>

<dialog
  bind:this={flushDialog}
  class="m-auto w-[calc(100%-2rem)] max-w-md rounded-xl p-6 shadow-xl backdrop:bg-black/50"
  aria-labelledby="flush-title"
>
  <h2 id="flush-title" class="text-lg font-semibold">
    {flushTarget !== null ? 'Purge this message?' : 'Flush dead letters?'}
  </h2>
  <p class="text-sm text-gray-600 mt-3">
    {#if flushTarget !== null}
      Permanently delete message <strong class="font-mono break-all"
        >{flushTarget}</strong
      >
    {:else}
      Permanently delete up to {limit.toLocaleString()} currently available messages
    {/if}
    from <strong class="break-all">{queueName}-dead-letter</strong>. This cannot
    be undone. Failed job history is preserved.
  </p>
  <p class="text-xs text-gray-500 mt-2">
    {#if flushTarget !== null}
      Other dead letters will remain in the queue.
    {:else}
      Leased messages and new failures may remain. Refresh afterward to verify
      the queue.
    {/if}
  </p>
  <div class="flex flex-wrap gap-2 justify-end mt-5">
    <Button variant="outline" onclick={() => flushDialog.close()}
      >{flushTarget !== null ? 'Keep message' : 'Keep messages'}</Button
    ><Button
      variant="destructive"
      disabled={busy || !isAdmin}
      onclick={() => {
        flushDialog.close();
        flush(flushTarget !== null ? 10000 : limit, flushTarget ?? undefined);
      }}
      >{flushTarget !== null ? 'Delete message' : 'Delete dead letters'}</Button
    >
  </div>
</dialog>
