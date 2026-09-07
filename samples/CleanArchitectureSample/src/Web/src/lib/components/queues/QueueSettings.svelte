<script lang="ts">
  import type { QueueSummary } from '$lib/types/queue';
  import { queueLabel } from './utils';
  let { queue }: { queue: QueueSummary } = $props();
</script>

<section class="p-5 space-y-6" aria-labelledby="settings-title">
  <div>
    <h2 id="settings-title" class="font-semibold">Queue settings</h2>
    <p class="text-sm text-gray-500 mt-1">
      Effective settings from the application's queue configuration. Changes to
      these settings take effect when the application restarts.
    </p>
  </div>
  <dl class="grid sm:grid-cols-2 lg:grid-cols-3 gap-5 text-sm">
    <div>
      <dt class="text-gray-500 text-xs">Display name</dt>
      <dd class="mt-1 font-medium break-words">{queueLabel(queue)}</dd>
    </div>
    <div>
      <dt class="text-gray-500 text-xs">Queue name</dt>
      <dd class="mt-1 font-mono text-xs break-all">{queue.queueName}</dd>
    </div>
    {#each [['Worker group', queue.group ?? 'None'], ['Concurrency', queue.concurrency], ['Prefetch count', queue.prefetchCount], ['Maximum attempts', queue.maxAttempts], ['Retry policy', queue.retryPolicy], ['Retry delays', queue.retryDelays ?? 'Default retry schedule'], ['Visibility timeout', `${queue.visibilityTimeoutSeconds} seconds`], ['Lease renewal', queue.autoRenewTimeout ? 'Automatic' : 'Manual'], ['Completion', queue.autoComplete ? 'Automatic after success' : 'Handler acknowledges'], ['Job tracking', queue.trackProgress ? 'Enabled' : 'Not enabled'], ['Subscription', queue.handlers.length > 1 ? 'Shared handler group' : 'Independent handler subscription'], ['Worker on responding API', queue.workerRunsHere ? (queue.isRunning === true ? 'Running here' : queue.isRunning === false ? 'Not running' : 'Unknown') : 'Separate worker process']] as [label, value]}
      <div>
        <dt class="text-gray-500 text-xs">{label}</dt>
        <dd class="mt-1 font-medium break-words">{value}</dd>
      </div>
    {/each}
  </dl>
  <div class="border-t pt-5">
    <h3 class="text-sm font-medium mb-2">Message type</h3>
    <p class="text-xs font-mono text-gray-600 break-all">
      {queue.messageType}
    </p>
  </div>
  <div>
    <h3 class="text-sm font-medium mb-2">
      Handlers ({queue.handlers.length})
    </h3>
    <ul class="space-y-2">
      {#each queue.handlers as handler}<li
          class="text-xs font-mono text-gray-600 break-all"
        >
          {handler}
        </li>{/each}
    </ul>
  </div>
</section>
