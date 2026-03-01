<script lang="ts">
  import type { Order, CreateOrderRequest, UpdateOrderRequest, OrderStatus } from '$lib/types/order';
  import { Input, Select, Button } from '$lib/components/ui';

  type Props = {
    order?: Order;
    onsubmit: (data: CreateOrderRequest | UpdateOrderRequest) => Promise<void>;
    loading?: boolean;
  };

  let { order, onsubmit, loading = false }: Props = $props();

  // svelte-ignore state_referenced_locally
  let customerId = $state(order?.customerId ?? '');
  // svelte-ignore state_referenced_locally
  let amount = $state(order?.amount ?? 0);
  // svelte-ignore state_referenced_locally
  let description = $state(order?.description ?? '');
  // svelte-ignore state_referenced_locally
  let status = $state<OrderStatus>(order?.status ?? 'Pending');
  let submitted = $state(false);
  // svelte-ignore state_referenced_locally
  let isEdit = !!order;

  // Validation
  let customerIdError = $derived(
    submitted && !isEdit && customerId.length < 3 ? 'Customer ID must be at least 3 characters' : undefined
  );
  let amountError = $derived(
    submitted && toNumber(amount) <= 0 ? 'Amount must be greater than 0' : undefined
  );
  let descriptionError = $derived(
    submitted && description.length < 5 ? 'Description must be at least 5 characters' : undefined
  );

  let isValid = $derived(
    (isEdit || customerId.length >= 3) && toNumber(amount) > 0 && description.length >= 5
  );

  // HTML number inputs may return strings; coerce to number for safe comparison/serialization
  function toNumber(v: string | number): number {
    return typeof v === 'string' ? Number(v) : v;
  }

  const statusOptions: { value: OrderStatus; label: string }[] = [
    { value: 'Pending', label: 'Pending' },
    { value: 'Confirmed', label: 'Confirmed' },
    { value: 'Processing', label: 'Processing' },
    { value: 'Shipped', label: 'Shipped' },
    { value: 'Delivered', label: 'Delivered' },
    { value: 'Cancelled', label: 'Cancelled' }
  ];

  async function handleSubmit(e: SubmitEvent) {
    e.preventDefault();
    submitted = true;
    if (!isValid) return;

    if (isEdit) {
      await onsubmit({ amount: toNumber(amount), description, status });
    } else {
      await onsubmit({ customerId, amount: toNumber(amount), description });
    }
  }
</script>

<form onsubmit={handleSubmit} class="space-y-4">
  {#if !isEdit}
    <Input
      label="Customer ID"
      bind:value={customerId}
      placeholder="Enter customer ID"
      minlength={3}
      maxlength={50}
      required
      error={customerIdError}
    />
  {:else}
    <div class="space-y-2">
      <span class="text-sm font-medium leading-none">Customer ID</span>
      <p class="text-sm text-muted-foreground">{order?.customerId}</p>
    </div>
  {/if}

  <Input
    type="number"
    label="Amount"
    bind:value={amount}
    min={0.01}
    max={1000000}
    step={0.01}
    required
    error={amountError}
  />

  <Input
    label="Description"
    bind:value={description}
    placeholder="Order description"
    minlength={5}
    maxlength={200}
    required
    error={descriptionError}
  />

  {#if isEdit}
    <Select label="Status" bind:value={status} options={statusOptions} />
  {/if}

  <div class="flex gap-2 pt-4">
    <Button type="submit" disabled={loading} {loading}>
      {isEdit ? 'Update Order' : 'Create Order'}
    </Button>
    <Button type="button" variant="secondary" href="/orders">Cancel</Button>
  </div>
</form>
