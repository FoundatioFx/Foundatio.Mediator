<script lang="ts">
  import { cn } from '$lib/utils';

  type Props = {
    text: string;
    variant?: 'default' | 'secondary' | 'destructive' | 'outline';
    class?: string;
  };

  let { text, variant = 'default', class: className }: Props = $props();

  const variants = {
    default: 'border-transparent bg-primary text-primary-foreground shadow',
    secondary: 'border-transparent bg-secondary text-secondary-foreground',
    destructive: 'border-transparent bg-destructive text-destructive-foreground shadow',
    outline: 'text-foreground'
  };
</script>

<span
  class={cn(
    'inline-flex items-center rounded-md border px-2.5 py-0.5 text-xs font-semibold transition-colors focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2',
    variants[variant],
    className
  )}
>
  {text}
</span>
