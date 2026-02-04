<script lang="ts">
  import type { Product, CreateProductRequest, UpdateProductRequest, ProductStatus } from '$lib/types/product';
  import { Input, Select, Button } from '$lib/components/ui';

  type Props = {
    product?: Product;
    onsubmit: (data: CreateProductRequest | UpdateProductRequest) => Promise<void>;
    loading?: boolean;
  };

  let { product, onsubmit, loading = false }: Props = $props();

  // Initialize form state - captures initial values intentionally
  const initialProduct = product;
  let name = $state(initialProduct?.name ?? '');
  let description = $state(initialProduct?.description ?? '');
  let price = $state(initialProduct?.price ?? 0);
  let stockQuantity = $state(initialProduct?.stockQuantity ?? 0);
  let status = $state<ProductStatus>(initialProduct?.status ?? 'Draft');

  let isValid = $derived(
    name.length >= 3 && description.length >= 5 && price > 0
  );

  const statusOptions: { value: ProductStatus; label: string }[] = [
    { value: 'Draft', label: 'Draft' },
    { value: 'Active', label: 'Active' },
    { value: 'OutOfStock', label: 'Out of Stock' },
    { value: 'Discontinued', label: 'Discontinued' }
  ];

  async function handleSubmit(e: SubmitEvent) {
    e.preventDefault();
    if (!isValid) return;

    if (initialProduct) {
      const updates: UpdateProductRequest = {};
      if (name !== initialProduct.name) updates.name = name;
      if (description !== initialProduct.description) updates.description = description;
      if (price !== initialProduct.price) updates.price = price;
      if (stockQuantity !== initialProduct.stockQuantity) updates.stockQuantity = stockQuantity;
      if (status !== initialProduct.status) updates.status = status;
      await onsubmit(updates);
    } else {
      await onsubmit({ name, description, price, stockQuantity });
    }
  }
</script>

<form onsubmit={handleSubmit} class="space-y-4">
  <Input
    label="Product Name"
    bind:value={name}
    placeholder="Enter product name"
    minlength={3}
    maxlength={100}
    required
  />

  <Input
    label="Description"
    bind:value={description}
    placeholder="Product description"
    minlength={5}
    maxlength={500}
    required
  />

  <div class="grid grid-cols-2 gap-4">
    <Input
      type="number"
      label="Price"
      bind:value={price}
      min={0.01}
      max={1000000}
      step={0.01}
      required
    />

    <Input
      type="number"
      label="Stock Quantity"
      bind:value={stockQuantity}
      min={0}
      max={1000000}
      step={1}
    />
  </div>

  {#if initialProduct}
    <Select label="Status" bind:value={status} options={statusOptions} />
  {/if}

  <div class="flex gap-2 pt-4">
    <Button type="submit" disabled={!isValid || loading} {loading}>
      {initialProduct ? 'Update Product' : 'Create Product'}
    </Button>
    <Button type="button" variant="secondary" href="/products">Cancel</Button>
  </div>
</form>
