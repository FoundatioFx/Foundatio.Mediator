<script lang="ts">
  import { toast } from '$lib/stores/toast.svelte';
  import Alert from '$lib/components/ui/Alert.svelte';
</script>

{#if toast.toasts.length > 0}
  <div class="fixed bottom-4 right-4 z-50 space-y-2 max-w-sm">
    {#each toast.toasts as t (t.id)}
      <Alert type={t.type} message={t.message} dismissible ondismiss={() => toast.remove(t.id)} />
    {/each}
  </div>
{/if}
