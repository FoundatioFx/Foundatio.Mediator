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
    success: 'border-green-500/50 text-green-600 [&>svg]:text-green-600',
    error: 'border-destructive/50 text-destructive [&>svg]:text-destructive',
    warning: 'border-yellow-500/50 text-yellow-600 [&>svg]:text-yellow-600',
    info: 'border-primary/50 text-primary [&>svg]:text-primary'
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
    'relative w-full rounded-lg border px-4 py-3 text-sm [&>svg+div]:translate-y-[-3px] [&>svg]:absolute [&>svg]:left-4 [&>svg]:top-4',
    variants[type],
    className
  )}
  role="alert"
>
  <Icon class="h-4 w-4" />
  <div class="pl-7">
    {#if title}
      <h5 class="mb-1 font-medium leading-none tracking-tight">{title}</h5>
    {/if}
    <div class="text-sm [&_p]:leading-relaxed">{message}</div>
  </div>
  {#if dismissible && ondismiss}
    <button
      type="button"
      class="absolute right-2 top-2 rounded-md p-1 opacity-70 hover:opacity-100 focus:outline-none focus:ring-1 focus:ring-ring"
      onclick={ondismiss}
    >
      <X class="h-4 w-4" />
    </button>
  {/if}
</div>
