<script lang="ts">
  import { Button } from '$lib/components/ui';

  let {
    polling = $bindable(),
    lastUpdated,
    manual,
    busy,
    refresh
  }: {
    polling: boolean;
    lastUpdated: number | null;
    manual: boolean;
    busy: boolean;
    refresh: () => void;
  } = $props();
</script>

<div class="flex flex-wrap items-center gap-2">
  <span
    class="text-xs text-gray-500 tabular-nums min-w-36"
    aria-label="Last refreshed"
  >
    {lastUpdated
      ? `Updated ${new Date(lastUpdated).toLocaleTimeString()}`
      : 'Connecting…'}
  </span>
  <Button size="sm" variant="outline" onclick={() => (polling = !polling)}
    >{polling ? 'Pause updates' : 'Resume updates'}</Button
  >
  <Button
    size="sm"
    variant="outline"
    disabled={manual || busy}
    onclick={refresh}>Refresh now</Button
  >
</div>
