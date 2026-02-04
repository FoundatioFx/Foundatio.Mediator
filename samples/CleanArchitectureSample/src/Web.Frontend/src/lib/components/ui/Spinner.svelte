<script lang="ts">
  import { cn } from '$lib/utils';
  import { LoaderCircle } from 'lucide-svelte';

  type Props = {
    size?: 'sm' | 'md' | 'lg';
    class?: string;
  };

  let { size = 'md', class: className }: Props = $props();

  const sizeClasses = {
    sm: 'h-4 w-4',
    md: 'h-8 w-8',
    lg: 'h-12 w-12'
  };
</script>

<LoaderCircle class={cn('animate-spin text-primary', sizeClasses[size], className)} />
