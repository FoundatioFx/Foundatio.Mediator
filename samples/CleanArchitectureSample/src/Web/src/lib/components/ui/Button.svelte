<script lang="ts">
  import type { Snippet } from 'svelte';
  import { cn } from '$lib/utils';
  import { LoaderCircle } from 'lucide-svelte';

  type Props = {
    type?: 'button' | 'submit' | 'reset';
    variant?: 'default' | 'destructive' | 'outline' | 'secondary' | 'ghost' | 'link';
    size?: 'default' | 'sm' | 'lg' | 'icon';
    disabled?: boolean;
    loading?: boolean;
    href?: string;
    class?: string;
    onclick?: () => void;
    children: Snippet;
  };

  let {
    type = 'button',
    variant = 'default',
    size = 'default',
    disabled = false,
    loading = false,
    href,
    class: className,
    onclick,
    children
  }: Props = $props();

  const baseStyles =
    'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50';

  const variants = {
    default: 'bg-primary text-primary-foreground shadow hover:bg-primary/90',
    destructive: 'bg-destructive text-destructive-foreground shadow-sm hover:bg-destructive/90',
    outline: 'border border-input bg-background shadow-sm hover:bg-accent hover:text-accent-foreground',
    secondary: 'bg-secondary text-secondary-foreground shadow-sm hover:bg-secondary/80',
    ghost: 'hover:bg-accent hover:text-accent-foreground',
    link: 'text-primary underline-offset-4 hover:underline'
  };

  const sizes = {
    default: 'h-9 px-4 py-2',
    sm: 'h-8 rounded-md px-3 text-xs',
    lg: 'h-10 rounded-md px-8',
    icon: 'h-9 w-9'
  };

  let buttonClass = $derived(cn(baseStyles, variants[variant], sizes[size], className));
</script>

{#if href && !disabled}
  <a {href} class={buttonClass}>
    {@render children()}
  </a>
{:else}
  <button {type} class={buttonClass} disabled={disabled || loading} {onclick}>
    {#if loading}
      <LoaderCircle class="h-4 w-4 animate-spin" />
    {/if}
    {@render children()}
  </button>
{/if}
