<script lang="ts">
  import { page } from '$app/stores';
  import { goto } from '$app/navigation';
  import { productsApi } from '$lib/api';
  import { ProductForm } from '$lib/components/products';
  import { Card, Spinner, Alert, Button } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import type { UpdateProductRequest } from '$lib/types/product';

  let productId = $derived($page.params.id);
  let productPromise = $state(productsApi.get($page.params.id));
  let loading = $state(false);

  $effect(() => {
    productPromise = productsApi.get(productId);
  });

  async function handleSubmit(data: UpdateProductRequest) {
    loading = true;
    try {
      await productsApi.update(productId, data);
      toast.success('Product updated successfully');
      goto('/products');
    } catch (error) {
      toast.error((error as Error).message || 'Failed to update product');
    } finally {
      loading = false;
    }
  }

  async function handleDelete() {
    if (!confirm('Are you sure you want to delete this product?')) return;

    try {
      await productsApi.delete(productId);
      toast.success('Product deleted successfully');
      goto('/products');
    } catch (error) {
      toast.error((error as Error).message || 'Failed to delete product');
    }
  }
</script>

<svelte:head>
  <title>Edit Product - Clean Architecture Sample</title>
</svelte:head>

<div class="max-w-2xl">
  {#await productPromise}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:then result}
    {#if result.data}
      <div class="mb-6 flex justify-between items-start">
        <div>
          <h1 class="text-2xl font-bold text-gray-900">Edit Product</h1>
          <p class="mt-1 text-sm text-gray-500">Product ID: {result.data.id}</p>
        </div>
        <Button variant="danger" onclick={handleDelete}>Delete Product</Button>
      </div>

      <Card>
        <ProductForm product={result.data} onsubmit={handleSubmit} {loading} />
      </Card>
    {:else}
      <Alert type="error" message="Product not found" />
      <div class="mt-4">
        <Button href="/products">Back to Products</Button>
      </div>
    {/if}
  {:catch error}
    <Alert type="error" message={error.message || 'Failed to load product'} />
    <div class="mt-4">
      <Button href="/products">Back to Products</Button>
    </div>
  {/await}
</div>
