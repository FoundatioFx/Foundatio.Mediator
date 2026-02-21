<script lang="ts">
  import { reportsApi } from '$lib/api';
  import { DashboardStats, RecentOrdersWidget, TopProductsWidget } from '$lib/components/reports';
  import { Button, Spinner, Alert } from '$lib/components/ui';

  let dashboardPromise = $state(reportsApi.dashboard());

  function refresh() {
    dashboardPromise = reportsApi.dashboard();
  }
</script>

<svelte:head>
  <title>Dashboard - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex justify-between items-center">
    <h1 class="text-2xl font-bold text-gray-900">Dashboard</h1>
    <Button variant="secondary" onclick={refresh}>Refresh</Button>
  </div>

  {#await dashboardPromise}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:then result}
    {#if result.data}
      <DashboardStats stats={result.data} />

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <RecentOrdersWidget orders={result.data.recentOrders} />
        <TopProductsWidget products={result.data.topProducts} />
      </div>
    {:else}
      <Alert type="error" message="Failed to load dashboard data" />
    {/if}
  {:catch error}
    <Alert type="error" message={error.message || 'Failed to load dashboard'} />
  {/await}
</div>
