<script lang="ts">
  import type { RecentOrder } from '$lib/types/report';
  import { Card } from '$lib/components/ui';
  import { OrderStatusBadge } from '$lib/components/orders';

  type Props = {
    orders: RecentOrder[];
  };

  let { orders }: Props = $props();

  function formatCurrency(value: number): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD'
    }).format(value);
  }

  function formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }
</script>

<Card title="Recent Orders">
  {#if orders.length === 0}
    <p class="text-muted-foreground text-sm">No recent orders</p>
  {:else}
    <div class="flow-root">
      <ul class="-my-5 divide-y divide-border">
        {#each orders.slice(0, 5) as order}
          <li class="py-4">
            <div class="flex items-center space-x-4">
              <div class="flex-1 min-w-0">
                <p class="text-sm font-medium text-foreground truncate">
                  {order.orderId}
                </p>
                <p class="text-sm text-muted-foreground truncate">
                  Customer: {order.customerId}
                </p>
              </div>
              <div class="text-right">
                <p class="text-sm font-medium text-foreground">
                  {formatCurrency(order.amount)}
                </p>
                <OrderStatusBadge status={order.status} />
              </div>
            </div>
            <p class="mt-1 text-xs text-muted-foreground">{formatDate(order.createdAt)}</p>
          </li>
        {/each}
      </ul>
    </div>
    <div class="mt-4">
      <a href="/orders" class="text-sm font-medium text-primary hover:text-primary/80">
        View all orders &rarr;
      </a>
    </div>
  {/if}
</Card>
