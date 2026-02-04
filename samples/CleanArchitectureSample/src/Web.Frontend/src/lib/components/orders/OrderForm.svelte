<script lang="ts">
  import type { Order, CreateOrderRequest, UpdateOrderRequest, OrderStatus } from '$lib/types/order';
  import { Input, Select, Button } from '$lib/components/ui';

  type Props = {
    order?: Order;
    onsubmit: (data: CreateOrderRequest | UpdateOrderRequest) => Promise<void>;
    loading?: boolean;
  };

  let { order, onsubmit, loading = false }: Props = $props();

  // Initialize form state - captures initial values intentionally
  const initialOrder = order;
  let customerId = $state(initialOrder?.customerId ?? '');
  let amount = $state(initialOrder?.amount ?? 0);
  let description = $state(initialOrder?.description ?? '');
  let status = $state<OrderStatus>(initialOrder?.status ?? 'Pending');

  let isValid = $derived(
    (initialOrder || customerId.length >= 3) && amount > 0 && description.length >= 5
  );

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
    if (!isValid) return;

    if (initialOrder) {
      const updates: UpdateOrderRequest = {};
      if (amount !== initialOrder.amount) updates.amount = amount;
      if (description !== initialOrder.description) updates.description = description;
      if (status !== initialOrder.status) updates.status = status;
      await onsubmit(updates);
    } else {
      await onsubmit({ customerId, amount, description });
    }
  }
</script>

<form onsubmit={handleSubmit} class="space-y-4">
  {#if !initialOrder}
    <Input
      label="Customer ID"
      bind:value={customerId}
      placeholder="Enter customer ID"
      minlength={3}
      maxlength={50}
      required
    />
  {:else}
    <div class="space-y-2">
      <span class="text-sm font-medium leading-none">Customer ID</span>
      <p class="text-sm text-muted-foreground">{initialOrder.customerId}</p>
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
  />

  <Input
    label="Description"
    bind:value={description}
    placeholder="Order description"
    minlength={5}
    maxlength={200}
    required
  />

  {#if initialOrder}
    <Select label="Status" bind:value={status} options={statusOptions} />
  {/if}

  <div class="flex gap-2 pt-4">
    <Button type="submit" disabled={!isValid || loading} {loading}>
      {initialOrder ? 'Update Order' : 'Create Order'}
    </Button>
    <Button type="button" variant="secondary" href="/orders">Cancel</Button>
  </div>
</form>
