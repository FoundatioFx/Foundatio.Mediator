<script lang="ts">
  import { goto } from '$app/navigation';
  import { ordersApi } from '$lib/api';
  import { OrderForm } from '$lib/components/orders';
  import { Card } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import type { CreateOrderRequest } from '$lib/types/order';

  let loading = $state(false);

  async function handleSubmit(data: CreateOrderRequest) {
    loading = true;
    try {
      await ordersApi.create(data);
      toast.success('Order created successfully');
      goto('/orders');
    } catch (error) {
      toast.error((error as Error).message || 'Failed to create order');
    } finally {
      loading = false;
    }
  }
</script>

<svelte:head>
  <title>New Order - Clean Architecture Sample</title>
</svelte:head>

<div class="max-w-2xl">
  <div class="mb-6">
    <h1 class="text-2xl font-bold text-gray-900">Create New Order</h1>
    <p class="mt-1 text-sm text-gray-500">Fill in the details below to create a new order.</p>
  </div>

  <Card>
    <OrderForm onsubmit={handleSubmit} {loading} />
  </Card>
</div>
