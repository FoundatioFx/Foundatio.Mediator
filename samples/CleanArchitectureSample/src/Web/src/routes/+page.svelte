<script lang="ts">
  import { onMount } from 'svelte';
  import { reportsApi } from '$lib/api';
  import { DashboardStats, RecentOrdersWidget, TopProductsWidget } from '$lib/components/reports';
  import { Button, Spinner, Alert } from '$lib/components/ui';
  import { eventStream } from '$lib/stores/eventstream.svelte';
  import type { DashboardReport } from '$lib/types/report';

  let dashboard = $state<DashboardReport | null>(null);
  let loading = $state(true);
  let error = $state<string | null>(null);

  async function loadDashboard() {
    try {
      const result = await reportsApi.dashboard();
      if (result.data) {
        dashboard = result.data;
        error = null;
      } else {
        error = 'Failed to load dashboard data';
      }
    } catch (e) {
      error = (e as Error).message || 'Failed to load dashboard';
    } finally {
      loading = false;
    }
  }

  async function refresh() {
    try {
      const result = await reportsApi.dashboard();
      if (result.data) {
        dashboard = result.data;
        error = null;
      }
    } catch {
      // Keep showing current data on background refresh failure
    }
  }

  onMount(() => {
    loadDashboard();

    // Refresh dashboard stats when any entity changes
    const unsubs = [
      eventStream.onOrderCreated(() => refresh()),
      eventStream.onOrderUpdated(() => refresh()),
      eventStream.onOrderDeleted(() => refresh()),
      eventStream.onProductCreated(() => refresh()),
      eventStream.onProductUpdated(() => refresh()),
      eventStream.onProductDeleted(() => refresh())
    ];

    return () => unsubs.forEach((unsub) => unsub());
  });
</script>

<svelte:head>
  <title>Dashboard - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex justify-between items-center">
    <h1 class="text-2xl font-bold text-gray-900">Dashboard</h1>
    <Button variant="secondary" onclick={refresh}>Refresh</Button>
  </div>

  {#if loading}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:else if error}
    <Alert type="error" message={error} />
  {:else if dashboard}
    <DashboardStats stats={dashboard} />

    <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
      <RecentOrdersWidget orders={dashboard.recentOrders} />
      <TopProductsWidget products={dashboard.topProducts} />
    </div>
  {/if}
</div>
