<script lang="ts">
  import { Card, Button } from '$lib/components/ui';
  import { BarChart3, Package, Search } from 'lucide-svelte';
</script>

<svelte:head>
  <title>Reports - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div>
    <h1 class="text-2xl font-bold text-foreground">Reports</h1>
    <p class="mt-1 text-sm text-muted-foreground">View analytics and insights about your business.</p>
  </div>

  <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
    <Card>
      <div class="flex flex-col items-center text-center p-4">
        <div class="h-12 w-12 rounded-lg bg-primary/10 flex items-center justify-center mb-4">
          <BarChart3 class="h-6 w-6 text-primary" />
        </div>
        <h3 class="font-semibold text-lg mb-2">Sales Report</h3>
        <p class="text-sm text-muted-foreground mb-4">
          View sales performance, revenue trends, and order statistics over time.
        </p>
        <Button href="/reports/sales">View Sales Report</Button>
      </div>
    </Card>

    <Card>
      <div class="flex flex-col items-center text-center p-4">
        <div class="h-12 w-12 rounded-lg bg-primary/10 flex items-center justify-center mb-4">
          <Package class="h-6 w-6 text-primary" />
        </div>
        <h3 class="font-semibold text-lg mb-2">Inventory Report</h3>
        <p class="text-sm text-muted-foreground mb-4">
          Monitor stock levels, identify low stock items, and track inventory value.
        </p>
        <Button href="/reports/inventory">View Inventory Report</Button>
      </div>
    </Card>

    <Card>
      <div class="flex flex-col items-center text-center p-4">
        <div class="h-12 w-12 rounded-lg bg-primary/10 flex items-center justify-center mb-4">
          <Search class="h-6 w-6 text-primary" />
        </div>
        <h3 class="font-semibold text-lg mb-2">Catalog Search</h3>
        <p class="text-sm text-muted-foreground mb-4">
          Search across products and orders to find specific items quickly.
        </p>
        <Button href="/reports/search">Search Catalog</Button>
      </div>
    </Card>
  </div>
</div>
