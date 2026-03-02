<script lang="ts">
  import { cn } from '$lib/utils';
  import { CircleCheck, CircleX, TriangleAlert, Info, X } from 'lucide-svelte';

  type AlertType = 'success' | 'error' | 'warning' | 'info';

  type Props = {
    type?: AlertType;
    title?: string;
    message: string;
    dismissible?: boolean;
    ondismiss?: () => void;
    class?: string;
  };

  let { type = 'info', title, message, dismissible = false, ondismiss, class: className }: Props = $props();

  const variants: Record<AlertType, string> = {
    success: 'border-green-500/50 bg-green-50 text-green-800',
    error: 'border-destructive/50 bg-red-50 text-red-800',
    warning: 'border-yellow-500/50 bg-yellow-50 text-yellow-800',
    info: 'border-primary/50 bg-blue-50 text-blue-800'
  };

  const iconVariants: Record<AlertType, string> = {
    success: 'text-green-600',
    error: 'text-destructive',
    warning: 'text-yellow-600',
    info: 'text-primary'
  };

  const icons: Record<AlertType, typeof CircleCheck> = {
    success: CircleCheck,
    error: CircleX,
    warning: TriangleAlert,
    info: Info
  };

  let Icon = $derived(icons[type]);
</script>

<div
  class={cn(
    'flex w-full items-start gap-3 rounded-lg border px-4 py-3 text-sm',
    variants[type],
    className
  )}
  role="alert"
>
  <Icon class={cn('mt-0.5 h-4 w-4 shrink-0', iconVariants[type])} />
  <div class="min-w-0 flex-1">
    {#if title}
      <h5 class="mb-1 font-medium leading-none tracking-tight">{title}</h5>
    {/if}
    <div class="text-sm leading-snug [&_p]:leading-relaxed">{message}</div>
  </div>
  {#if dismissible && ondismiss}
    <button
      type="button"
      class="shrink-0 rounded-md p-0.5 opacity-70 transition-opacity hover:opacity-100 focus:outline-none focus:ring-1 focus:ring-ring"
      onclick={ondismiss}
    >
      <X class="h-4 w-4" />
    </button>
  {/if}
</div>
