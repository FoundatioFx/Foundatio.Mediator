<script lang="ts">
  import type { ProductStatus } from '$lib/types/product';
  import { Badge } from '$lib/components/ui';

  type Props = {
    status: ProductStatus;
  };

  let { status }: Props = $props();

  const statusStyles: Record<ProductStatus, string> = {
    Draft: 'bg-gray-100 text-gray-800 border-gray-200',
    Active: 'bg-green-100 text-green-800 border-green-200',
    OutOfStock: 'bg-red-100 text-red-800 border-red-200',
    Discontinued: 'bg-orange-100 text-orange-800 border-orange-200'
  };
</script>

<Badge text={status} class={statusStyles[status]} />
