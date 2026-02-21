<script lang="ts">
  import type { DashboardReport } from '$lib/types/report';
  import { ClipboardList, Package, DollarSign, AlertTriangle } from 'lucide-svelte';

  type Props = {
    stats: DashboardReport;
  };

  let { stats }: Props = $props();

  function formatCurrency(value: number): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD'
    }).format(value);
  }

  const statCards = $derived([
    { label: 'Total Orders', value: stats.totalOrders.toString(), color: 'bg-blue-500', icon: ClipboardList },
    { label: 'Total Products', value: stats.totalProducts.toString(), color: 'bg-green-500', icon: Package },
    { label: 'Total Revenue', value: formatCurrency(stats.totalRevenue), color: 'bg-purple-500', icon: DollarSign },
    { label: 'Low Stock Items', value: stats.lowStockProductCount.toString(), color: 'bg-red-500', icon: AlertTriangle }
  ]);
</script>

<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
  {#each statCards as stat}
    <div class="bg-white rounded-lg shadow-sm border border-gray-200 p-6">
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <div class="w-12 h-12 rounded-lg {stat.color} flex items-center justify-center">
            <stat.icon class="w-6 h-6 text-white" />
          </div>
        </div>
        <div class="ml-4">
          <p class="text-sm font-medium text-gray-500">{stat.label}</p>
          <p class="text-2xl font-semibold text-gray-900">{stat.value}</p>
        </div>
      </div>
    </div>
  {/each}
</div>
