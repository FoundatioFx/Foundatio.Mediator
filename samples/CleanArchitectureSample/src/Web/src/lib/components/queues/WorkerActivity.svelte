<script lang="ts">
  import { auth } from '$lib/stores/auth.svelte';
  import { eventStream } from '$lib/stores/eventstream.svelte';
  import { queueUrl } from './utils';

  const activity = $derived(
    eventStream.events
      .filter((event) => typeof event.data.queueName === 'string')
      .slice(0, 8)
  );
</script>

<section class="rounded-lg border bg-white p-5 space-y-4">
  <div class="flex flex-wrap justify-between gap-3">
    <div>
      <h2 class="font-semibold">Live worker activity</h2>
      <p class="text-xs text-gray-500 mt-1">
        Best-effort notifications from workers to this API's event feed. Job
        details remain the source for status.
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
      No worker events received during this session.
    </p>{:else}<ul class="divide-y">
      {#each activity as event (event.id)}<li
          class="py-2 flex flex-wrap gap-2 justify-between text-sm"
        >
          <span
            >{event.type}
            <a
              class="text-xs text-blue-700 underline break-all"
              href={queueUrl(String(event.data.queueName))}
              >{String(event.data.queueName)}</a
            >
            <span class="text-xs text-gray-500"
              >{event.host ?? 'Unknown host'}</span
            ></span
          ><span class="text-xs text-gray-400"
            >{event.timestamp.toLocaleTimeString()}{#if typeof event.data.jobId === 'string'}
              · <a
                class="text-blue-700 underline"
                href={queueUrl(
                  String(event.data.queueName),
                  'jobs',
                  event.data.jobId
                )}>Open job</a
              >{/if}</span
          >
        </li>{/each}
    </ul>{/if}<a class="text-xs text-blue-700 underline" href="/events"
    >Open the full event feed</a
  >
</section>
