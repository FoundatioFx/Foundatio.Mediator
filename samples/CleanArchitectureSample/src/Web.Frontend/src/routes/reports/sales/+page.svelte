<script lang="ts">
  import { reportsApi } from '$lib/api';
  import { Card, Button, Input, Spinner, Alert } from '$lib/components/ui';

  let startDate = $state('');
  let endDate = $state('');
  let salesPromise = $state(reportsApi.sales());

  function search() {
    salesPromise = reportsApi.sales(startDate || undefined, endDate || undefined);
  }

  function formatCurrency(value: number): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD'
    }).format(value);
  }

  function formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    });
  }
</script>

<svelte:head>
  <title>Sales Report - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex justify-between items-center">
    <div>
      <h1 class="text-2xl font-bold text-foreground">Sales Report</h1>
      <p class="mt-1 text-sm text-muted-foreground">View sales performance and revenue trends.</p>
    </div>
    <Button variant="outline" href="/reports">Back to Reports</Button>
  </div>

  <Card title="Date Range Filter">
    <div class="flex flex-wrap gap-4 items-end">
      <div class="flex-1 min-w-[200px]">
        <Input type="date" label="Start Date" bind:value={startDate} />
      </div>
      <div class="flex-1 min-w-[200px]">
        <Input type="date" label="End Date" bind:value={endDate} />
      </div>
      <Button onclick={search}>Apply Filter</Button>
    </div>
  </Card>

  {#await salesPromise}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:then result}
    {#if result.data}
      <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
        <Card>
          <div class="text-center">
            <p class="text-sm text-muted-foreground">Total Orders</p>
            <p class="text-3xl font-bold text-foreground">{result.data.orderCount}</p>
          </div>
        </Card>
        <Card>
          <div class="text-center">
            <p class="text-sm text-muted-foreground">Total Revenue</p>
            <p class="text-3xl font-bold text-foreground">{formatCurrency(result.data.totalRevenue)}</p>
          </div>
        </Card>
        <Card>
          <div class="text-center">
            <p class="text-sm text-muted-foreground">Average Order Value</p>
            <p class="text-3xl font-bold text-foreground">{formatCurrency(result.data.averageOrderValue)}</p>
          </div>
        </Card>
      </div>

      {#if result.data.dailySales.length > 0}
        <Card title="Daily Sales">
          <div class="overflow-x-auto">
            <table class="min-w-full divide-y divide-border">
              <thead>
                <tr>
                  <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Date</th>
                  <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Orders</th>
                  <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Revenue</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-border">
                {#each result.data.dailySales as day}
                  <tr class="hover:bg-muted/50">
                    <td class="px-4 py-3 text-sm text-foreground">{formatDate(day.date)}</td>
                    <td class="px-4 py-3 text-sm text-muted-foreground">{day.orderCount}</td>
                    <td class="px-4 py-3 text-sm text-muted-foreground">{formatCurrency(day.revenue)}</td>
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        </Card>
      {:else}
        <Alert type="info" message="No sales data available for the selected date range." />
      {/if}
    {:else}
      <Alert type="error" message="Failed to load sales report" />
    {/if}
  {:catch error}
    <Alert type="error" message={error.message || 'Failed to load sales report'} />
  {/await}
</div>
