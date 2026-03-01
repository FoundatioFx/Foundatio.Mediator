<script lang="ts">
  import { page } from '$app/stores';
  import { goto } from '$app/navigation';
  import { onMount } from 'svelte';
  import { ordersApi } from '$lib/api';
  import { OrderForm } from '$lib/components/orders';
  import { AuthGuard } from '$lib/components/layout';
  import { Card, Spinner, Alert, Button } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import type { Order, UpdateOrderRequest } from '$lib/types/order';

  let orderId = $derived($page.params.id!);
  let order = $state<Order | null>(null);
  let pageLoading = $state(true);
  let saving = $state(false);
  let error = $state<string | null>(null);

  onMount(() => {
    loadOrder();
  });

  async function loadOrder() {
    pageLoading = true;
    error = null;
    try {
      const result = await ordersApi.get(orderId);
      order = result.data ?? null;
      if (!order) error = 'Order not found';
    } catch (e) {
      error = (e as Error).message || 'Failed to load order';
    } finally {
      pageLoading = false;
    }
  }

  async function handleSubmit(data: UpdateOrderRequest) {
    saving = true;
    try {
      await ordersApi.update(orderId, data);
      toast.success('Order updated successfully');
      goto('/orders');
    } catch (e) {
      toast.error((e as Error).message || 'Failed to update order');
    } finally {
      saving = false;
    }
  }

  async function handleDelete() {
    if (!confirm('Are you sure you want to delete this order?')) return;

    try {
      await ordersApi.delete(orderId);
      toast.success('Order deleted successfully');
      goto('/orders');
    } catch (e) {
      toast.error((e as Error).message || 'Failed to delete order');
    }
  }
</script>

<svelte:head>
  <title>Edit Order - Clean Architecture Sample</title>
</svelte:head>

<AuthGuard>
<div class="max-w-2xl">
  {#if pageLoading}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:else if error}
    <Alert type="error" message={error} />
    <div class="mt-4">
      <Button href="/orders">Back to Orders</Button>
    </div>
  {:else if order}
    <div class="mb-6 flex justify-between items-start">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">Edit Order</h1>
        <p class="mt-1 text-sm text-gray-500">Order ID: {order.id}</p>
      </div>
      <Button variant="destructive" onclick={handleDelete}>Delete Order</Button>
    </div>

    <Card>
      <OrderForm {order} onsubmit={handleSubmit} loading={saving} />
    </Card>
  {/if}
</div>
</AuthGuard>
