<script lang="ts">
  import type { Snippet } from 'svelte';
  import { cn } from '$lib/utils';

  type Props = {
    title?: string;
    description?: string;
    class?: string;
    children: Snippet;
    actions?: Snippet;
    footer?: Snippet;
  };

  let { title, description, class: className, children, actions, footer }: Props = $props();
</script>

<div class={cn('rounded-xl border bg-card text-card-foreground shadow', className)}>
  {#if title || description || actions}
    <div class="flex flex-col space-y-1.5 p-6">
      <div class="flex items-center justify-between">
        {#if title}
          <h3 class="font-semibold leading-none tracking-tight">{title}</h3>
        {/if}
        {#if actions}
          <div class="flex gap-2">
            {@render actions()}
          </div>
        {/if}
      </div>
      {#if description}
        <p class="text-sm text-muted-foreground">{description}</p>
      {/if}
    </div>
  {/if}
  <div class={cn('p-6', (title || description || actions) && 'pt-0')}>
    {@render children()}
  </div>
  {#if footer}
    <div class="flex items-center p-6 pt-0">
      {@render footer()}
    </div>
  {/if}
</div>
