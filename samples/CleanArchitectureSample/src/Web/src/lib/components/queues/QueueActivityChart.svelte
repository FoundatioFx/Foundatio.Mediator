<script lang="ts">
  import type { CounterBucket } from '$lib/types/queue';

  let { queueName, buckets }: { queueName: string; buckets: CounterBucket[] } =
    $props();
  const series = [
    { key: 'processed', label: 'Completed', color: '#16a34a', dash: '' },
    { key: 'failed', label: 'Failed attempts', color: '#ea580c', dash: '7 3' },
    {
      key: 'dead_lettered',
      label: 'Dead-lettered',
      color: '#dc2626',
      dash: '2 3'
    }
  ];
  let containerWidth = $state(0);
  let selectedHour = $state<string | null>(null);
  let chart: SVGSVGElement;
  const width = $derived(Math.max(containerWidth, 280));
  const height = 270;
  const left = 52;
  const right = 16;
  const top = 20;
  const bottom = height - 42;
  const plotWidth = $derived(width - left - right);
  const binWidth = $derived(plotWidth / Math.max(buckets.length, 1));
  const selectedIndex = $derived.by(() => {
    const index = buckets.findIndex((bucket) => bucket.hour === selectedHour);
    return index < 0 ? Math.max(0, buckets.length - 1) : index;
  });
  const selected = $derived(buckets[selectedIndex]);
  const maximum = $derived(
    Math.max(
      0,
      ...buckets.flatMap((bucket) =>
        series.map(({ key }) => bucket.counters[key] ?? 0)
      )
    )
  );
  const tickStep = $derived.by(() => {
    const rough = Math.max(1, maximum / 4);
    const power = 10 ** Math.floor(Math.log10(rough));
    return [1, 2, 5, 10].find((step) => step * power >= rough)! * power;
  });
  const ceiling = $derived(
    Math.max(1, Math.ceil(maximum / tickStep)) * tickStep
  );
  const ticks = $derived(
    Array.from(
      { length: Math.round(ceiling / tickStep) + 1 },
      (_, i) => i * tickStep
    )
  );
  const hourTicks = $derived.by(() => {
    const count = Math.min(
      buckets.length,
      Math.max(2, Math.floor(plotWidth / 110))
    );
    return [
      ...new Set(
        Array.from({ length: count }, (_, i) =>
          Math.round((i * (buckets.length - 1)) / Math.max(1, count - 1))
        )
      )
    ];
  });
  const y = (value: number) => bottom - (value / ceiling) * (bottom - top);
  const x = (index: number) => left + index * binWidth;
  const time = (hour: string) =>
    new Date(hour).toLocaleTimeString([], {
      hour: 'numeric',
      minute: '2-digit'
    });
  const date = (hour: string) =>
    new Date(hour).toLocaleString([], {
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      timeZoneName: 'short'
    });
  const compact = new Intl.NumberFormat(undefined, {
    notation: 'compact',
    maximumFractionDigits: 1
  });

  function line(key: string): string {
    return buckets
      .map(
        (bucket, index) =>
          `${index === 0 ? `M ${x(0)}` : 'V'} ${y(bucket.counters[key] ?? 0)} H ${x(index + 1)}`
      )
      .join(' ');
  }
  function description(bucket: CounterBucket) {
    return `${date(bucket.hour)}: ${series.map(({ key, label }) => `${label}: ${(bucket.counters[key] ?? 0).toLocaleString()}`).join('; ')}`;
  }
  function select(index: number) {
    selectedHour = buckets[index].hour;
  }
  function move(event: KeyboardEvent, index: number) {
    const next =
      event.key === 'ArrowRight'
        ? Math.min(index + 1, buckets.length - 1)
        : event.key === 'ArrowLeft'
          ? Math.max(index - 1, 0)
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? buckets.length - 1
              : null;
    if (next !== null) {
      event.preventDefault();
      select(next);
      chart.querySelector<SVGGElement>(`[data-hour-index="${next}"]`)?.focus();
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      select(index);
    }
  }
</script>

<div class="rounded-lg border bg-white p-4 min-w-0">
  <div class="flex flex-wrap items-center justify-between gap-3">
    <p class="text-xs text-gray-500">Count per hour · local time</p>
    <div class="flex flex-wrap gap-x-5 gap-y-2" aria-label="Chart legend">
      {#each series as item (item.key)}
        <span class="inline-flex items-center gap-2 text-xs text-gray-600">
          <svg width="24" height="8" aria-hidden="true"
            ><line
              x1="0"
              x2="24"
              y1="4"
              y2="4"
              stroke={item.color}
              stroke-width="2.5"
              stroke-dasharray={item.dash}
            /></svg
          >
          {item.label}
        </span>
      {/each}
    </div>
  </div>
  <div bind:clientWidth={containerWidth} class="mt-3 min-w-0">
    <svg
      bind:this={chart}
      viewBox={`0 0 ${width} ${height}`}
      class="block w-full"
      role="group"
      aria-label={`Hourly queue activity for ${queueName}`}
    >
      <title>Completed, failed, and dead-lettered counts by hour</title>
      <desc
        >Each step covers one hour. Select an hour for exact counts. Use left
        and right arrow keys to move between hours.</desc
      >
      {#each ticks as tick}
        <line
          x1={left}
          x2={width - right}
          y1={y(tick)}
          y2={y(tick)}
          stroke="#e5e7eb"
          stroke-dasharray={tick === 0 ? undefined : '3 4'}
        />
        <text
          x={left - 10}
          y={y(tick)}
          dy="0.35em"
          text-anchor="end"
          fill="#6b7280"
          font-size="11">{compact.format(tick)}</text
        >
      {/each}
      {#if selected}
        <rect
          x={x(selectedIndex)}
          y={top}
          width={binWidth}
          height={bottom - top}
          fill="#f1f5f9"
        />
      {/if}
      {#each series as item (item.key)}
        <path
          d={`${line(item.key)} L ${x(buckets.length)} ${bottom} L ${left} ${bottom} Z`}
          fill={item.color}
          fill-opacity="0.06"
        />
        <path
          d={line(item.key)}
          fill="none"
          stroke={item.color}
          stroke-width="2"
          stroke-dasharray={item.dash}
          stroke-linejoin="round"
          data-series={item.key}
        />
      {/each}
      {#each hourTicks as index}
        <text
          x={x(index) + binWidth / 2}
          y={bottom + 24}
          text-anchor={index === 0
            ? 'start'
            : index === buckets.length - 1
              ? 'end'
              : 'middle'}
          fill="#6b7280"
          font-size="11">{time(buckets[index].hour)}</text
        >
      {/each}
      {#each buckets as bucket, index (bucket.hour)}
        <g
          role="button"
          tabindex={selectedIndex === index ? 0 : -1}
          aria-label={description(bucket)}
          aria-pressed={selectedIndex === index}
          data-hour-index={index}
          onpointerenter={() => select(index)}
          onfocus={() => select(index)}
          onclick={() => select(index)}
          onkeydown={(event) => move(event, index)}
          class="cursor-crosshair outline-none"
        >
          <rect
            x={x(index)}
            y={top - 4}
            width={binWidth}
            height={bottom - top + 8}
            fill="transparent"
          />
          {#if selectedIndex === index}
            {#each series as item (item.key)}
              <circle
                cx={x(index) + binWidth / 2}
                cy={y(bucket.counters[item.key] ?? 0)}
                r="3"
                fill="white"
                stroke={item.color}
                stroke-width="2"
                pointer-events="none"
              />
            {/each}
          {/if}
        </g>
      {/each}
    </svg>
  </div>
  {#if selected}
    <div
      class="border-t pt-3 flex flex-wrap items-center justify-between gap-3 text-xs"
      role="status"
      aria-label="Selected hour"
    >
      <span class="text-gray-500">{date(selected.hour)}</span>
      <dl class="flex flex-wrap gap-x-5 gap-y-2">
        {#each series as item (item.key)}
          <div class="flex items-center gap-2">
            <dt class="text-gray-500">{item.label}</dt>
            <dd class="font-semibold tabular-nums" style:color={item.color}>
              {(selected.counters[item.key] ?? 0).toLocaleString()}
            </dd>
          </div>
        {/each}
      </dl>
    </div>
  {/if}
  <p class="mt-3 text-xs text-gray-400">
    Hover or select an hour for details. Use arrow keys when the chart is
    focused.
  </p>
</div>
