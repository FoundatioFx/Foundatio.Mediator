<script lang="ts">
  import { page } from '$app/state';
  import QueueDetails from '$lib/components/queues/QueueDetails.svelte';

  const queueName = $derived(page.params.name!);
</script>

{#key queueName}
  <QueueDetails {queueName} />
{/key}
