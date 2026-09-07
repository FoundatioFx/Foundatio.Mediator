<script lang="ts">
  import { page } from '$app/stores';
  import QueueDetails from '$lib/components/queues/QueueDetails.svelte';

  const queueName = $derived($page.params.name!);
</script>

<svelte:head>
  <title>{queueName} - Queue details - Clean Architecture Sample</title>
</svelte:head>

{#key queueName}
  <QueueDetails {queueName} />
{/key}
