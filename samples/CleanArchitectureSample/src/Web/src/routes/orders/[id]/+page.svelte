<script lang="ts">
  import { page } from '$app/stores';
  import { goto } from '$app/navigation';
  import { ordersApi } from '$lib/api';
  import { OrderForm } from '$lib/components/orders';
  import { Card, Spinner, Alert, Button } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import type { UpdateOrderRequest } from '$lib/types/order';

  let orderId = $derived($page.params.id);
  let orderPromise = $state(ordersApi.get($page.params.id));
  let loading = $state(false);

  $effect(() => {
    orderPromise = ordersApi.get(orderId);
  });

  async function handleSubmit(data: UpdateOrderRequest) {
    loading = true;
    try {
      await ordersApi.update(orderId, data);
      toast.success('Order updated successfully');
      goto('/orders');
    } catch (error) {
      toast.error((error as Error).message || 'Failed to update order');
    } finally {
      loading = false;
    }
  }

  async function handleDelete() {
    if (!confirm('Are you sure you want to delete this order?')) return;

    try {
      await ordersApi.delete(orderId);
      toast.success('Order deleted successfully');
      goto('/orders');
    } catch (error) {
      toast.error((error as Error).message || 'Failed to delete order');
    }
  }
</script>

<svelte:head>
  <title>Edit Order - Clean Architecture Sample</title>
</svelte:head>

<div class="max-w-2xl">
  {#await orderPromise}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:then result}
    {#if result.data}
      <div class="mb-6 flex justify-between items-start">
        <div>
          <h1 class="text-2xl font-bold text-gray-900">Edit Order</h1>
          <p class="mt-1 text-sm text-gray-500">Order ID: {result.data.id}</p>
        </div>
        <Button variant="danger" onclick={handleDelete}>Delete Order</Button>
      </div>

      <Card>
        <OrderForm order={result.data} onsubmit={handleSubmit} {loading} />
      </Card>
    {:else}
      <Alert type="error" message="Order not found" />
      <div class="mt-4">
        <Button href="/orders">Back to Orders</Button>
      </div>
    {/if}
  {:catch error}
    <Alert type="error" message={error.message || 'Failed to load order'} />
    <div class="mt-4">
      <Button href="/orders">Back to Orders</Button>
    </div>
  {/await}
</div>
