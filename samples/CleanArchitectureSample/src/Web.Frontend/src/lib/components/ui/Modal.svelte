<script lang="ts">
  import type { Snippet } from 'svelte';
  import { cn } from '$lib/utils';
  import { X } from 'lucide-svelte';

  type Props = {
    open: boolean;
    title: string;
    description?: string;
    onclose: () => void;
    children: Snippet;
    actions?: Snippet;
    class?: string;
  };

  let { open, title, description, onclose, children, actions, class: className }: Props = $props();

  function handleKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') {
      onclose();
    }
  }
</script>

{#if open}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <div
    class="fixed inset-0 z-50 bg-black/80"
    role="button"
    tabindex="-1"
    onclick={onclose}
    onkeydown={handleKeydown}
  ></div>

  <!-- Modal -->
  <div
    class={cn(
      'fixed left-[50%] top-[50%] z-50 grid w-full max-w-lg translate-x-[-50%] translate-y-[-50%] gap-4 border bg-background p-6 shadow-lg duration-200 sm:rounded-lg',
      className
    )}
    role="dialog"
    aria-modal="true"
    aria-labelledby="modal-title"
  >
    <div class="flex flex-col space-y-1.5 text-center sm:text-left">
      <div class="flex items-center justify-between">
        <h2 id="modal-title" class="text-lg font-semibold leading-none tracking-tight">{title}</h2>
        <button
          type="button"
          class="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
          onclick={onclose}
        >
          <X class="h-4 w-4" />
          <span class="sr-only">Close</span>
        </button>
      </div>
      {#if description}
        <p class="text-sm text-muted-foreground">{description}</p>
      {/if}
    </div>
    <div>
      {@render children()}
    </div>
    {#if actions}
      <div class="flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2">
        {@render actions()}
      </div>
    {/if}
  </div>
{/if}
