<script lang="ts">
  import { cn } from '$lib/utils';

  type Props = {
    type?: 'text' | 'number' | 'email' | 'password' | 'date' | 'search';
    label?: string;
    placeholder?: string;
    value?: string | number;
    required?: boolean;
    disabled?: boolean;
    min?: number;
    max?: number;
    step?: number;
    minlength?: number;
    maxlength?: number;
    error?: string;
    class?: string;
    onkeydown?: (e: KeyboardEvent) => void;
  };

  let {
    type = 'text',
    label,
    placeholder,
    value = $bindable(),
    required = false,
    disabled = false,
    min,
    max,
    step,
    minlength,
    maxlength,
    error,
    class: className,
    onkeydown
  }: Props = $props();

  const inputId = crypto.randomUUID();
</script>

<div class="space-y-2">
  {#if label}
    <label
      for={inputId}
      class="text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70"
    >
      {label}
      {#if required}<span class="text-destructive">*</span>{/if}
    </label>
  {/if}
  <input
    id={inputId}
    {type}
    class={cn(
      'flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-base shadow-sm transition-colors file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-foreground placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 md:text-sm',
      error && 'border-destructive focus-visible:ring-destructive',
      className
    )}
    {placeholder}
    bind:value
    {required}
    {disabled}
    {min}
    {max}
    {step}
    {minlength}
    {maxlength}
    {onkeydown}
  />
  {#if error}
    <p class="text-sm text-destructive">{error}</p>
  {/if}
</div>
