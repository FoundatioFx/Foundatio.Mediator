<script lang="ts">
  import type { TopProduct } from '$lib/types/report';
  import { Card } from '$lib/components/ui';
  import { ProductStatusBadge } from '$lib/components/products';

  type Props = {
    products: TopProduct[];
  };

  let { products }: Props = $props();

  function formatCurrency(value: number): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD'
    }).format(value);
  }
</script>

<Card title="Top Products">
  {#if products.length === 0}
    <p class="text-muted-foreground text-sm">No products available</p>
  {:else}
    <div class="flow-root">
      <ul class="-my-5 divide-y divide-border">
        {#each products.slice(0, 5) as product}
          <li class="py-4">
            <div class="flex items-center space-x-4">
              <div class="flex-1 min-w-0">
                <p class="text-sm font-medium text-foreground truncate">
                  {product.name}
                </p>
                <p class="text-sm text-muted-foreground">
                  Stock: {product.stockQuantity}
                </p>
              </div>
              <div class="text-right">
                <p class="text-sm font-medium text-foreground">
                  {formatCurrency(product.price)}
                </p>
                <ProductStatusBadge status={product.status} />
              </div>
            </div>
          </li>
        {/each}
      </ul>
    </div>
    <div class="mt-4">
      <a href="/products" class="text-sm font-medium text-primary hover:text-primary/80">
        View all products &rarr;
      </a>
    </div>
  {/if}
</Card>
