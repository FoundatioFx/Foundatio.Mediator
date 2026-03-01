<script lang="ts">
  import type { Product, CreateProductRequest, UpdateProductRequest, ProductStatus } from '$lib/types/product';
  import { Input, Select, Button } from '$lib/components/ui';

  type Props = {
    product?: Product;
    onsubmit: (data: CreateProductRequest | UpdateProductRequest) => Promise<void>;
    loading?: boolean;
  };

  let { product, onsubmit, loading = false }: Props = $props();

  // svelte-ignore state_referenced_locally
  let name = $state(product?.name ?? '');
  // svelte-ignore state_referenced_locally
  let description = $state(product?.description ?? '');
  // svelte-ignore state_referenced_locally
  let price = $state(product?.price ?? 0);
  // svelte-ignore state_referenced_locally
  let stockQuantity = $state(product?.stockQuantity ?? 0);
  // svelte-ignore state_referenced_locally
  let status = $state<ProductStatus>(product?.status ?? 'Draft');
  let submitted = $state(false);
  // svelte-ignore state_referenced_locally
  let isEdit = !!product;

  // Validation
  let nameError = $derived(
    submitted && name.length < 3 ? 'Name must be at least 3 characters' : undefined
  );
  let descriptionError = $derived(
    submitted && description.length < 5 ? 'Description must be at least 5 characters' : undefined
  );
  let priceError = $derived(
    submitted && toNumber(price) <= 0 ? 'Price must be greater than 0' : undefined
  );

  let isValid = $derived(
    name.length >= 3 && description.length >= 5 && toNumber(price) > 0
  );

  // HTML number inputs may return strings; coerce to number for safe comparison/serialization
  function toNumber(v: string | number): number {
    return typeof v === 'string' ? Number(v) : v;
  }

  const statusOptions: { value: ProductStatus; label: string }[] = [
    { value: 'Draft', label: 'Draft' },
    { value: 'Active', label: 'Active' },
    { value: 'OutOfStock', label: 'Out of Stock' },
    { value: 'Discontinued', label: 'Discontinued' }
  ];

  async function handleSubmit(e: SubmitEvent) {
    e.preventDefault();
    submitted = true;
    if (!isValid) return;

    if (isEdit) {
      await onsubmit({ name, description, price: toNumber(price), stockQuantity: toNumber(stockQuantity), status });
    } else {
      await onsubmit({ name, description, price: toNumber(price), stockQuantity: toNumber(stockQuantity) });
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
    error={nameError}
  />

  <Input
    label="Description"
    bind:value={description}
    placeholder="Product description"
    minlength={5}
    maxlength={500}
    required
    error={descriptionError}
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
      error={priceError}
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

  {#if isEdit}
    <Select label="Status" bind:value={status} options={statusOptions} />
  {/if}

  <div class="flex gap-2 pt-4">
    <Button type="submit" disabled={loading} {loading}>
      {isEdit ? 'Update Product' : 'Create Product'}
    </Button>
    <Button type="button" variant="secondary" href="/products">Cancel</Button>
  </div>
</form>
