<script lang="ts">
  interface Props {
    data: number[];
    width?: number;
    height?: number;
    color?: string;
    fillOpacity?: number;
    label?: string;
  }

  let { data, width = 120, height = 32, color = '#3b82f6', fillOpacity = 0.15, label = '' }: Props = $props();

  const total = $derived(data.reduce((a, b) => a + b, 0));

  /** Ensure we always have at least 2 points so the sparkline renders a baseline. */
  const safeData = $derived(data.length >= 2 ? data : Array(2).fill(0));

  const polyline = $derived.by(() => {
    const max = Math.max(...safeData, 1);
    const step = width / (safeData.length - 1);
    const pad = 1;
    return safeData
      .map((v, i) => `${i * step},${height - pad - ((v / max) * (height - 2 * pad))}`)
      .join(' ');
  });

  const fillPath = $derived.by(() => {
    const max = Math.max(...safeData, 1);
    const step = width / (safeData.length - 1);
    const pad = 1;
    const points = safeData.map((v, i) => `${i * step},${height - pad - ((v / max) * (height - 2 * pad))}`);
    return `M0,${height} L${points.join(' L')} L${width},${height} Z`;
  });
</script>

<div class="inline-flex items-center gap-1.5" title={label ? `${label}: ${total.toLocaleString()}` : undefined}>
  <svg {width} {height} class="overflow-visible">
    <path d={fillPath} fill={color} opacity={fillOpacity} />
    <polyline
      points={polyline}
      fill="none"
      stroke={color}
      stroke-width="1.5"
      stroke-linecap="round"
      stroke-linejoin="round"
    />
  </svg>
  {#if label}
    <span class="text-xs text-gray-500 tabular-nums">{total.toLocaleString()}</span>
  {/if}
</div>
