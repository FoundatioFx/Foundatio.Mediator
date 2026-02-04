<script lang="ts">
  import { ordersApi } from '$lib/api';
  import { OrderList } from '$lib/components/orders';
  import { Button, Spinner, Alert } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import type { Order } from '$lib/types/order';

  let ordersPromise = $state(ordersApi.list());

  function refresh() {
    ordersPromise = ordersApi.list();
  }

  async function handleDelete(order: Order) {
    if (!confirm(`Are you sure you want to delete order ${order.id}?`)) return;

    try {
      await ordersApi.delete(order.id);
      toast.success('Order deleted successfully');
      refresh();
    } catch (error) {
      toast.error((error as Error).message || 'Failed to delete order');
    }
  }
</script>

<svelte:head>
  <title>Orders - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex justify-between items-center">
    <h1 class="text-2xl font-bold text-gray-900">Orders</h1>
    <div class="flex gap-2">
      <Button variant="secondary" onclick={refresh}>Refresh</Button>
      <Button href="/orders/new">New Order</Button>
    </div>
  </div>

  {#await ordersPromise}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:then result}
    {#if result.data}
      <OrderList orders={result.data} ondelete={handleDelete} />
    {:else}
      <Alert type="error" message="Failed to load orders" />
    {/if}
  {:catch error}
    <Alert type="error" message={error.message || 'Failed to load orders'} />
  {/await}
</div>
