<script lang="ts">
  import type { OrderStatus } from '$lib/types/order';
  import { Badge } from '$lib/components/ui';

  type Props = {
    status: OrderStatus;
  };

  let { status }: Props = $props();

  const statusStyles: Record<OrderStatus, string> = {
    Pending: 'bg-yellow-100 text-yellow-800 border-yellow-200',
    Confirmed: 'bg-blue-100 text-blue-800 border-blue-200',
    Processing: 'bg-purple-100 text-purple-800 border-purple-200',
    Shipped: 'bg-indigo-100 text-indigo-800 border-indigo-200',
    Delivered: 'bg-green-100 text-green-800 border-green-200',
    Cancelled: 'bg-red-100 text-red-800 border-red-200'
  };
</script>

<Badge text={status} class={statusStyles[status]} />
