<script lang="ts">
  import type { Product } from '$lib/types/product';
  import { Button } from '$lib/components/ui';
  import ProductStatusBadge from './ProductStatusBadge.svelte';

  type Props = {
    products: Product[];
    ondelete?: (product: Product) => void;
  };

  let { products, ondelete }: Props = $props();

  function formatCurrency(value: number): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD'
    }).format(value);
  }

  function formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    });
  }
</script>

{#if products.length === 0}
  <div class="text-center py-12">
    <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
    </svg>
    <h3 class="mt-2 text-sm font-medium text-gray-900">No products</h3>
    <p class="mt-1 text-sm text-gray-500">Get started by creating a new product.</p>
    <div class="mt-6">
      <Button href="/products/new">Create Product</Button>
    </div>
  </div>
{:else}
  <div class="overflow-hidden shadow ring-1 ring-black ring-opacity-5 rounded-lg">
    <table class="min-w-full divide-y divide-gray-300">
      <thead class="bg-gray-50">
        <tr>
          <th class="py-3.5 pl-4 pr-3 text-left text-sm font-semibold text-gray-900">Product</th>
          <th class="px-3 py-3.5 text-left text-sm font-semibold text-gray-900">Price</th>
          <th class="px-3 py-3.5 text-left text-sm font-semibold text-gray-900">Stock</th>
          <th class="px-3 py-3.5 text-left text-sm font-semibold text-gray-900">Status</th>
          <th class="px-3 py-3.5 text-left text-sm font-semibold text-gray-900">Created</th>
          <th class="relative py-3.5 pl-3 pr-4">
            <span class="sr-only">Actions</span>
          </th>
        </tr>
      </thead>
      <tbody class="divide-y divide-gray-200 bg-white">
        {#each products as product}
          <tr class="hover:bg-gray-50">
            <td class="py-4 pl-4 pr-3">
              <div class="flex flex-col">
                <span class="text-sm font-medium text-gray-900">{product.name}</span>
                <span class="text-sm text-gray-500 truncate max-w-xs">{product.description}</span>
              </div>
            </td>
            <td class="whitespace-nowrap px-3 py-4 text-sm text-gray-500">{formatCurrency(product.price)}</td>
            <td class="whitespace-nowrap px-3 py-4 text-sm text-gray-500">
              <span class:text-red-600={product.stockQuantity < 10}>{product.stockQuantity}</span>
            </td>
            <td class="whitespace-nowrap px-3 py-4 text-sm">
              <ProductStatusBadge status={product.status} />
            </td>
            <td class="whitespace-nowrap px-3 py-4 text-sm text-gray-500">{formatDate(product.createdAt)}</td>
            <td class="relative whitespace-nowrap py-4 pl-3 pr-4 text-right text-sm font-medium">
              <a href="/products/{product.id}" class="text-blue-600 hover:text-blue-900 mr-4">Edit</a>
              {#if ondelete}
                <button type="button" class="text-red-600 hover:text-red-900" onclick={() => ondelete(product)}>
                  Delete
                </button>
              {/if}
            </td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
{/if}
