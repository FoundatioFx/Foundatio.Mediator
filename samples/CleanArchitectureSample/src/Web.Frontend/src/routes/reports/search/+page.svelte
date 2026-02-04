<script lang="ts">
  import { reportsApi } from '$lib/api';
  import { Card, Button, Input, Spinner, Alert } from '$lib/components/ui';
  import { OrderStatusBadge } from '$lib/components/orders';
  import { ProductStatusBadge } from '$lib/components/products';
  import type { CatalogSearchResult } from '$lib/types/report';
  import { Search } from 'lucide-svelte';

  let searchTerm = $state('');
  let searchPromise = $state<Promise<{ data: CatalogSearchResult | null }> | null>(null);

  function search() {
    if (searchTerm.trim().length < 2) return;
    searchPromise = reportsApi.searchCatalog(searchTerm);
  }

  function handleKeydown(e: KeyboardEvent) {
    if (e.key === 'Enter') {
      search();
    }
  }

  function formatCurrency(value: number): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD'
    }).format(value);
  }
</script>

<svelte:head>
  <title>Catalog Search - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex justify-between items-center">
    <div>
      <h1 class="text-2xl font-bold text-foreground">Catalog Search</h1>
      <p class="mt-1 text-sm text-muted-foreground">Search across products and orders.</p>
    </div>
    <Button variant="outline" href="/reports">Back to Reports</Button>
  </div>

  <Card>
    <div class="flex gap-4">
      <div class="flex-1">
        <Input
          type="search"
          placeholder="Search for products or orders..."
          bind:value={searchTerm}
          onkeydown={handleKeydown}
        />
      </div>
      <Button onclick={search} disabled={searchTerm.trim().length < 2}>
        <Search class="h-4 w-4 mr-2" />
        Search
      </Button>
    </div>
  </Card>

  {#if searchPromise}
    {#await searchPromise}
      <div class="flex justify-center py-12">
        <Spinner size="lg" />
      </div>
    {:then result}
      {#if result.data}
        <div class="space-y-6">
          <p class="text-sm text-muted-foreground">
            Found {result.data.products.length} products and {result.data.orders.length} orders matching "{result.data.searchTerm}"
          </p>

          {#if result.data.products.length > 0}
            <Card title="Products">
              <div class="overflow-x-auto">
                <table class="min-w-full divide-y divide-border">
                  <thead>
                    <tr>
                      <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Product</th>
                      <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Description</th>
                      <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Price</th>
                      <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Status</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-border">
                    {#each result.data.products as product}
                      <tr class="hover:bg-muted/50">
                        <td class="px-4 py-3">
                          <a href="/products/{product.productId}" class="text-sm font-medium text-primary hover:underline">
                            {product.name}
                          </a>
                        </td>
                        <td class="px-4 py-3 text-sm text-muted-foreground max-w-xs truncate">
                          {product.description}
                        </td>
                        <td class="px-4 py-3 text-sm text-muted-foreground">{formatCurrency(product.price)}</td>
                        <td class="px-4 py-3">
                          <ProductStatusBadge status={product.status} />
                        </td>
                      </tr>
                    {/each}
                  </tbody>
                </table>
              </div>
            </Card>
          {/if}

          {#if result.data.orders.length > 0}
            <Card title="Orders">
              <div class="overflow-x-auto">
                <table class="min-w-full divide-y divide-border">
                  <thead>
                    <tr>
                      <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Order ID</th>
                      <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Customer</th>
                      <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Description</th>
                      <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Amount</th>
                      <th class="px-4 py-3 text-left text-sm font-semibold text-foreground">Status</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-border">
                    {#each result.data.orders as order}
                      <tr class="hover:bg-muted/50">
                        <td class="px-4 py-3">
                          <a href="/orders/{order.orderId}" class="text-sm font-medium text-primary hover:underline">
                            {order.orderId}
                          </a>
                        </td>
                        <td class="px-4 py-3 text-sm text-muted-foreground">{order.customerId}</td>
                        <td class="px-4 py-3 text-sm text-muted-foreground max-w-xs truncate">
                          {order.description}
                        </td>
                        <td class="px-4 py-3 text-sm text-muted-foreground">{formatCurrency(order.amount)}</td>
                        <td class="px-4 py-3">
                          <OrderStatusBadge status={order.status} />
                        </td>
                      </tr>
                    {/each}
                  </tbody>
                </table>
              </div>
            </Card>
          {/if}

          {#if result.data.products.length === 0 && result.data.orders.length === 0}
            <Alert type="info" message="No results found. Try a different search term." />
          {/if}
        </div>
      {:else}
        <Alert type="error" message="Failed to search catalog" />
      {/if}
    {:catch error}
      <Alert type="error" message={error.message || 'Search failed'} />
    {/await}
  {:else}
    <div class="text-center py-12">
      <Search class="mx-auto h-12 w-12 text-muted-foreground" />
      <h3 class="mt-2 text-sm font-medium text-foreground">Start searching</h3>
      <p class="mt-1 text-sm text-muted-foreground">Enter a search term to find products and orders.</p>
    </div>
  {/if}
</div>
