<script lang="ts">
  import { reportsApi } from '$lib/api';
  import { Card, Button, Spinner, Alert, Badge } from '$lib/components/ui';

  let inventoryPromise = $state(reportsApi.inventory());

  function refresh() {
    inventoryPromise = reportsApi.inventory();
  }

  function formatCurrency(value: number): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD'
    }).format(value);
  }
</script>

<svelte:head>
  <title>Inventory Report - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex justify-between items-center">
    <div>
      <h1 class="text-2xl font-bold text-foreground">Inventory Report</h1>
      <p class="mt-1 text-sm text-muted-foreground">Monitor stock levels and inventory value.</p>
    </div>
    <div class="flex gap-2">
      <Button variant="secondary" onclick={refresh}>Refresh</Button>
      <Button variant="outline" href="/reports">Back to Reports</Button>
    </div>
  </div>

  {#await inventoryPromise}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:then result}
    {#if result.data}
      <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-4">
        <Card>
          <div class="text-center">
            <p class="text-sm text-muted-foreground">Total Products</p>
            <p class="text-2xl font-bold text-foreground">{result.data.totalProducts}</p>
          </div>
        </Card>
        <Card>
          <div class="text-center">
            <p class="text-sm text-muted-foreground">Active Products</p>
            <p class="text-2xl font-bold text-green-600">{result.data.activeProducts}</p>
          </div>
        </Card>
        <Card>
          <div class="text-center">
            <p class="text-sm text-muted-foreground">Out of Stock</p>
            <p class="text-2xl font-bold text-red-600">{result.data.outOfStockProducts}</p>
          </div>
        </Card>
        <Card>
          <div class="text-center">
            <p class="text-sm text-muted-foreground">Low Stock</p>
            <p class="text-2xl font-bold text-yellow-600">{result.data.lowStockProducts}</p>
          </div>
        </Card>
        <Card>
          <div class="text-center">
            <p class="text-sm text-muted-foreground">Inventory Value</p>
            <p class="text-2xl font-bold text-foreground">{formatCurrency(result.data.totalInventoryValue)}</p>
          </div>
        </Card>
      </div>

      {#if result.data.lowStockItems.length > 0}
        <Card title="Low Stock Items">
          <div class="overflow-x-auto">
            <table class="min-w-full divide-y divide-border">
              <thead>
                <tr>
                  <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Product</th>
                  <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Current Stock</th>
                  <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Reorder Threshold</th>
                  <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Status</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-border">
                {#each result.data.lowStockItems as item}
                  <tr class="hover:bg-muted/50">
                    <td class="px-4 py-3">
                      <a href="/products/{item.productId}" class="text-sm font-medium text-primary hover:underline">
                        {item.name}
                      </a>
                    </td>
                    <td class="px-4 py-3 text-sm text-red-600 font-medium">{item.stockQuantity}</td>
                    <td class="px-4 py-3 text-sm text-muted-foreground">{item.reorderThreshold}</td>
                    <td class="px-4 py-3">
                      <Badge
                        text={item.stockQuantity === 0 ? 'Out of Stock' : 'Low Stock'}
                        class={item.stockQuantity === 0 ? 'bg-red-100 text-red-800' : 'bg-yellow-100 text-yellow-800'}
                      />
                    </td>
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        </Card>
      {:else}
        <Alert type="success" message="All products have sufficient stock levels." />
      {/if}
    {:else}
      <Alert type="error" message="Failed to load inventory report" />
    {/if}
  {:catch error}
    <Alert type="error" message={error.message || 'Failed to load inventory report'} />
  {/await}
</div>
