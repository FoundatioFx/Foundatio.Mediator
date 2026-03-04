<script lang="ts">
  import { onMount } from 'svelte';
  import { afterNavigate } from '$app/navigation';
  import { ordersApi } from '$lib/api';
  import { OrderList } from '$lib/components/orders';
  import { AuthGuard } from '$lib/components/layout';
  import { Button, Spinner, Alert } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import { eventStream } from '$lib/stores/eventstream.svelte';
  import type { Order } from '$lib/types/order';

  let orders = $state<Order[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let highlightedIds = $state(new Set<string>());
  let highlightTimers = new Map<string, ReturnType<typeof setTimeout>>();

  function highlightItem(id: string) {
    const existing = highlightTimers.get(id);
    if (existing) clearTimeout(existing);

    highlightedIds = new Set([...highlightedIds, id]);

    highlightTimers.set(
      id,
      setTimeout(() => {
        const next = new Set(highlightedIds);
        next.delete(id);
        highlightedIds = next;
        highlightTimers.delete(id);
      }, 2500)
    );
  }

  async function loadOrders() {
    try {
      const result = await ordersApi.list();
      if (result.data) {
        orders = result.data;
        error = null;
      } else {
        error = 'Failed to load orders';
      }
    } catch (e) {
      error = (e as Error).message || 'Failed to load orders';
    } finally {
      loading = false;
    }
  }

  async function refresh() {
    try {
      const result = await ordersApi.list();
      if (result.data) {
        orders = result.data;
        error = null;
      }
    } catch {
      // Keep showing current data on background refresh failure
    }
  }

  async function handleDelete(order: Order) {
    if (!confirm(`Are you sure you want to delete order ${order.id}?`)) return;

    try {
      await ordersApi.delete(order.id);
      toast.success('Order deleted successfully');
      orders = orders.filter((o) => o.id !== order.id);
    } catch (e) {
      toast.error((e as Error).message || 'Failed to delete order');
    }
  }

  // Reload orders whenever the user navigates to this page (including back from edit/create)
  // Reload on SPA navigations back to this page
  afterNavigate((nav) => {
    if (nav.from) loadOrders();
  });

  onMount(() => {
    // Initial data load — afterNavigate may miss the first render when
    // the layout delays mounting children (e.g. auth check)
    loadOrders();

    const unsubCreated = eventStream.onOrderCreated((event) => {
      toast.success('New order created');
      refresh().then(() => highlightItem(event.orderId));
    });

    const unsubUpdated = eventStream.onOrderUpdated((event) => {
      toast.info('Order updated');
      refresh().then(() => highlightItem(event.orderId));
    });

    const unsubDeleted = eventStream.onOrderDeleted((event) => {
      toast.info('Order deleted');
      orders = orders.filter((o) => o.id !== event.orderId);
    });

    return () => {
      unsubCreated();
      unsubUpdated();
      unsubDeleted();
      highlightTimers.forEach((timer) => clearTimeout(timer));
    };
  });
</script>

<svelte:head>
  <title>Orders - Clean Architecture Sample</title>
</svelte:head>

<AuthGuard>
<div class="space-y-6">
  <div class="flex justify-between items-center">
    <h1 class="text-2xl font-bold text-gray-900">Orders</h1>
    <div class="flex gap-2">
      <Button variant="secondary" onclick={refresh}>Refresh</Button>
      <Button href="/orders/new">New Order</Button>
    </div>
  </div>

  {#if loading}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:else if error}
    <Alert type="error" message={error} />
  {:else}
    <OrderList {orders} ondelete={handleDelete} {highlightedIds} />
  {/if}
</div>
</AuthGuard>
