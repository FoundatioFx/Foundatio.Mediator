<script lang="ts">
  import { goto } from '$app/navigation';
  import { productsApi } from '$lib/api';
  import { ProductForm } from '$lib/components/products';
  import { AuthGuard } from '$lib/components/layout';
  import { Card } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import type { CreateProductRequest, UpdateProductRequest } from '$lib/types/product';

  let loading = $state(false);

  async function handleSubmit(data: CreateProductRequest | UpdateProductRequest) {
    const createData = data as CreateProductRequest;
    loading = true;
    try {
      await productsApi.create(createData);
      toast.success('Product created successfully');
      goto('/products');
    } catch (error) {
      toast.error((error as Error).message || 'Failed to create product');
    } finally {
      loading = false;
    }
  }
</script>

<svelte:head>
  <title>New Product - Clean Architecture Sample</title>
</svelte:head>

<AuthGuard>
<div class="max-w-2xl">
  <div class="mb-6">
    <h1 class="text-2xl font-bold text-gray-900">Create New Product</h1>
    <p class="mt-1 text-sm text-gray-500">Fill in the details below to create a new product.</p>
  </div>

  <Card>
    <ProductForm onsubmit={handleSubmit} {loading} />
  </Card>
</div>
</AuthGuard>
