<script lang="ts" generics="T extends string">
  import { cn } from '$lib/utils';

  type Option = {
    value: T;
    label: string;
  };

  type Props = {
    label?: string;
    options: Option[];
    value?: T;
    required?: boolean;
    disabled?: boolean;
    class?: string;
  };

  let { label, options, value = $bindable(), required = false, disabled = false, class: className }: Props = $props();

  const selectId = crypto.randomUUID();
</script>

<div class="space-y-2">
  {#if label}
    <label
      for={selectId}
      class="text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70"
    >
      {label}
      {#if required}<span class="text-destructive">*</span>{/if}
    </label>
  {/if}
  <select
    id={selectId}
    class={cn(
      'flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-base shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 md:text-sm',
      className
    )}
    bind:value
    {required}
    {disabled}
  >
    {#each options as option}
      <option value={option.value}>{option.label}</option>
    {/each}
  </select>
</div>
