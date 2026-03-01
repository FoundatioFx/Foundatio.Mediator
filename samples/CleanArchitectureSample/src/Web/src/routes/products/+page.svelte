<script lang="ts">
  import { onMount } from 'svelte';
  import { afterNavigate } from '$app/navigation';
  import { productsApi } from '$lib/api';
  import { ProductList } from '$lib/components/products';
  import { Button, Spinner, Alert } from '$lib/components/ui';
  import { toast } from '$lib/stores/toast.svelte';
  import { signalr } from '$lib/stores/signalr.svelte';
  import { auth } from '$lib/stores/auth.svelte';
  import type { Product } from '$lib/types/product';

  let products = $state<Product[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let highlightedIds = $state(new Set<string>());
  let highlightTimers = new Map<string, ReturnType<typeof setTimeout>>();

  function highlightItem(id: string) {
    const existing = highlightTimers.get(id);
    if (existing) clearTimeout(existing);

    highlightedIds = new Set([...highlightedIds, id]);

    highlightTimers.set(
      id,
      setTimeout(() => {
        const next = new Set(highlightedIds);
        next.delete(id);
        highlightedIds = next;
        highlightTimers.delete(id);
      }, 2500)
    );
  }

  async function loadProducts() {
    try {
      const result = await productsApi.list();
      if (result.data) {
        products = result.data;
        error = null;
      } else {
        error = 'Failed to load products';
      }
    } catch (e) {
      error = (e as Error).message || 'Failed to load products';
    } finally {
      loading = false;
    }
  }

  async function refresh() {
    try {
      const result = await productsApi.list();
      if (result.data) {
        products = result.data;
        error = null;
      }
    } catch {
      // Keep showing current data on background refresh failure
    }
  }

  async function handleDelete(product: Product) {
    if (!confirm(`Are you sure you want to delete "${product.name}"?`)) return;

    try {
      await productsApi.delete(product.id);
      toast.success('Product deleted successfully');
      products = products.filter((p) => p.id !== product.id);
    } catch (e) {
      toast.error((e as Error).message || 'Failed to delete product');
    }
  }

  // Reload products whenever the user navigates to this page (including back from edit/create)
  // Reload on SPA navigations back to this page
  afterNavigate((nav) => {
    if (nav.from) loadProducts();
  });

  onMount(() => {
    // Initial data load — afterNavigate may miss the first render when
    // the layout delays mounting children (e.g. auth check)
    loadProducts();

    const unsubCreated = signalr.onProductCreated((event) => {
      toast.success('New product created');
      refresh().then(() => highlightItem(event.productId));
    });

    const unsubUpdated = signalr.onProductUpdated((event) => {
      toast.info('Product updated');
      refresh().then(() => highlightItem(event.productId));
    });

    const unsubDeleted = signalr.onProductDeleted((event) => {
      toast.info('Product deleted');
      products = products.filter((p) => p.id !== event.productId);
    });

    return () => {
      unsubCreated();
      unsubUpdated();
      unsubDeleted();
      highlightTimers.forEach((timer) => clearTimeout(timer));
    };
  });
</script>

<svelte:head>
  <title>Products - Clean Architecture Sample</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex justify-between items-center">
    <h1 class="text-2xl font-bold text-gray-900">Products</h1>
    <div class="flex gap-2">
      <Button variant="secondary" onclick={refresh}>Refresh</Button>
      {#if auth.isAuthenticated}
        <Button href="/products/new">New Product</Button>
      {/if}
    </div>
  </div>

  {#if loading}
    <div class="flex justify-center py-12">
      <Spinner size="lg" />
    </div>
  {:else if error}
    <Alert type="error" message={error} />
  {:else}
    <ProductList {products} ondelete={auth.isAuthenticated ? handleDelete : undefined} {highlightedIds} />
  {/if}
</div>
