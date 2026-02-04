<script lang="ts">
  import type { Order } from '$lib/types/order';
  import { Button } from '$lib/components/ui';
  import OrderStatusBadge from './OrderStatusBadge.svelte';

  type Props = {
    orders: Order[];
    ondelete?: (order: Order) => void;
  };

  let { orders, ondelete }: Props = $props();

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
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }
</script>

{#if orders.length === 0}
  <div class="text-center py-12">
    <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
    </svg>
    <h3 class="mt-2 text-sm font-medium text-gray-900">No orders</h3>
    <p class="mt-1 text-sm text-gray-500">Get started by creating a new order.</p>
    <div class="mt-6">
      <Button href="/orders/new">Create Order</Button>
    </div>
  </div>
{:else}
  <div class="overflow-hidden shadow ring-1 ring-black ring-opacity-5 rounded-lg">
    <table class="min-w-full divide-y divide-gray-300">
      <thead class="bg-gray-50">
        <tr>
          <th class="py-3.5 pl-4 pr-3 text-left text-sm font-semibold text-gray-900">Order ID</th>
          <th class="px-3 py-3.5 text-left text-sm font-semibold text-gray-900">Customer</th>
          <th class="px-3 py-3.5 text-left text-sm font-semibold text-gray-900">Amount</th>
          <th class="px-3 py-3.5 text-left text-sm font-semibold text-gray-900">Status</th>
          <th class="px-3 py-3.5 text-left text-sm font-semibold text-gray-900">Created</th>
          <th class="relative py-3.5 pl-3 pr-4">
            <span class="sr-only">Actions</span>
          </th>
        </tr>
      </thead>
      <tbody class="divide-y divide-gray-200 bg-white">
        {#each orders as order}
          <tr class="hover:bg-gray-50">
            <td class="whitespace-nowrap py-4 pl-4 pr-3 text-sm font-medium text-gray-900">
              {order.id}
            </td>
            <td class="whitespace-nowrap px-3 py-4 text-sm text-gray-500">{order.customerId}</td>
            <td class="whitespace-nowrap px-3 py-4 text-sm text-gray-500">{formatCurrency(order.amount)}</td>
            <td class="whitespace-nowrap px-3 py-4 text-sm">
              <OrderStatusBadge status={order.status} />
            </td>
            <td class="whitespace-nowrap px-3 py-4 text-sm text-gray-500">{formatDate(order.createdAt)}</td>
            <td class="relative whitespace-nowrap py-4 pl-3 pr-4 text-right text-sm font-medium">
              <a href="/orders/{order.id}" class="text-blue-600 hover:text-blue-900 mr-4">Edit</a>
              {#if ondelete}
                <button type="button" class="text-red-600 hover:text-red-900" onclick={() => ondelete(order)}>
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
