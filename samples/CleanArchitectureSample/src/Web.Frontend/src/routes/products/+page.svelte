<script lang="ts">
  import { productsApi } from '$lib/api';
  import { ProductList } from '$lib/components/products';
  import { Button, Spinner, Alert } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import type { Product } from '$lib/types/product';

  let productsPromise = $state(productsApi.list());

  function refresh() {
    productsPromise = productsApi.list();
  }

  async function handleDelete(product: Product) {
    if (!confirm(`Are you sure you want to delete "${product.name}"?`)) return;

    try {
      await productsApi.delete(product.id);
      toast.success('Product deleted successfully');
      refresh();
    } catch (error) {
      toast.error((error as Error).message || 'Failed to delete product');
    }
  }
</script>

<svelte:head>
  <title>Products - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex justify-between items-center">
    <h1 class="text-2xl font-bold text-gray-900">Products</h1>
    <div class="flex gap-2">
      <Button variant="secondary" onclick={refresh}>Refresh</Button>
      <Button href="/products/new">New Product</Button>
    </div>
  </div>

  {#await productsPromise}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:then result}
    {#if result.data}
      <ProductList products={result.data} ondelete={handleDelete} />
    {:else}
      <Alert type="error" message="Failed to load products" />
    {/if}
  {:catch error}
    <Alert type="error" message={error.message || 'Failed to load products'} />
  {/await}
</div>
