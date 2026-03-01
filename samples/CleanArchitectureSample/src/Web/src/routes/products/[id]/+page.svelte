<script lang="ts">
  import { page } from '$app/stores';
  import { goto } from '$app/navigation';
  import { onMount } from 'svelte';
  import { productsApi } from '$lib/api';
  import { ProductForm } from '$lib/components/products';
  import { AuthGuard } from '$lib/components/layout';
  import { Card, Spinner, Alert, Button } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import type { Product, UpdateProductRequest } from '$lib/types/product';

  let productId = $derived($page.params.id!);
  let product = $state<Product | null>(null);
  let pageLoading = $state(true);
  let saving = $state(false);
  let error = $state<string | null>(null);

  onMount(() => {
    loadProduct();
  });

  async function loadProduct() {
    pageLoading = true;
    error = null;
    try {
      const result = await productsApi.get(productId);
      product = result.data ?? null;
      if (!product) error = 'Product not found';
    } catch (e) {
      error = (e as Error).message || 'Failed to load product';
    } finally {
      pageLoading = false;
    }
  }

  async function handleSubmit(data: UpdateProductRequest) {
    saving = true;
    try {
      await productsApi.update(productId, data);
      toast.success('Product updated successfully');
      goto('/products');
    } catch (e) {
      toast.error((e as Error).message || 'Failed to update product');
    } finally {
      saving = false;
    }
  }

  async function handleDelete() {
    if (!confirm('Are you sure you want to delete this product?')) return;

    try {
      await productsApi.delete(productId);
      toast.success('Product deleted successfully');
      goto('/products');
    } catch (e) {
      toast.error((e as Error).message || 'Failed to delete product');
    }
  }
</script>

<svelte:head>
  <title>Edit Product - Clean Architecture Sample</title>
</svelte:head>

<AuthGuard>
<div class="max-w-2xl">
  {#if pageLoading}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:else if error}
    <Alert type="error" message={error} />
    <div class="mt-4">
      <Button href="/products">Back to Products</Button>
    </div>
  {:else if product}
    <div class="mb-6 flex justify-between items-start">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">Edit Product</h1>
        <p class="mt-1 text-sm text-gray-500">Product ID: {product.id}</p>
      </div>
      <Button variant="destructive" onclick={handleDelete}>Delete Product</Button>
    </div>

    <Card>
      <ProductForm {product} onsubmit={handleSubmit} loading={saving} />
    </Card>
  {/if}
</div>
</AuthGuard>
