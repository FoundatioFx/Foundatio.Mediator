<script lang="ts">
  import { page } from '$app/stores';
  import { auth } from '$lib/stores/auth.svelte';

  const publicItems = [
    { href: '/', label: 'Dashboard', icon: 'M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6' },
    { href: '/products', label: 'Products', icon: 'M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4' },
    { href: '/reports/search', label: 'Search Catalog', icon: 'M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z' },
    { href: '/events', label: 'Live Events', icon: 'M13 10V3L4 14h7v7l9-11h-7z' },
  ];

  const adminItems = [
    { href: '/orders', label: 'Orders', icon: 'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-3 7h3m-3 4h3m-6-4h.01M9 16h.01' },
    { href: '/reports', label: 'Reports', icon: 'M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z' },
  ];

  const allHrefs = [...publicItems, ...adminItems].map((i) => i.href);

  function isActive(href: string, pathname: string): boolean {
    if (href === '/') return pathname === '/';
    if (pathname === href) return true;
    if (!pathname.startsWith(href + '/')) return false;
    // Only highlight if no other nav item is a more specific match
    return !allHrefs.some((h) => h !== href && h.startsWith(href + '/') && (pathname === h || pathname.startsWith(h + '/')));
  }
</script>

<aside class="hidden lg:flex lg:shrink-0">
  <div class="flex flex-col w-64">
    <div class="flex flex-col grow bg-white border-r border-gray-200 pt-5 pb-4 overflow-y-auto">
      <nav class="mt-5 flex-1 px-2 space-y-1">
        {#each publicItems as item}
          <a
            href={item.href}
            class="group flex items-center px-2 py-2 text-sm font-medium rounded-md transition-colors {isActive(item.href, $page.url.pathname)
              ? 'bg-blue-50 text-blue-600'
              : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'}"
          >
            <svg
              class="mr-3 h-5 w-5 shrink-0 {isActive(item.href, $page.url.pathname)
                ? 'text-blue-600'
                : 'text-gray-400 group-hover:text-gray-500'}"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d={item.icon} />
            </svg>
            {item.label}
          </a>
        {/each}

        {#if auth.isAuthenticated}
          <div class="border-t border-gray-200 my-3"></div>
          <p class="px-2 text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">Admin</p>
          {#each adminItems as item}
            <a
              href={item.href}
              class="group flex items-center px-2 py-2 text-sm font-medium rounded-md transition-colors {isActive(item.href, $page.url.pathname)
                ? 'bg-blue-50 text-blue-600'
                : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'}"
            >
              <svg
                class="mr-3 h-5 w-5 shrink-0 {isActive(item.href, $page.url.pathname)
                  ? 'text-blue-600'
                  : 'text-gray-400 group-hover:text-gray-500'}"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d={item.icon} />
              </svg>
              {item.label}
            </a>
          {/each}
        {/if}
      </nav>
    </div>
  </div>
</aside>
