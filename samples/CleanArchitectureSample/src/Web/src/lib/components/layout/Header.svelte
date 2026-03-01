<script lang="ts">
  import { page } from '$app/stores';
  import { auth } from '$lib/stores/auth.svelte';
</script>

<header class="bg-white shadow-sm border-b border-gray-200">
  <div class="px-4 sm:px-6 lg:px-8">
    <div class="flex h-12 justify-between items-center">
      <div class="flex items-center">
        <a href="/" class="text-xl font-bold text-gray-900">
          Clean Architecture
        </a>
      </div>

      <nav class="hidden md:flex items-stretch space-x-8 self-stretch -mb-px">
        <!-- Public nav links - always visible -->
        <a
          href="/"
          class="inline-flex items-center border-b-2 text-sm font-medium transition-colors {$page.url.pathname === '/'
            ? 'border-blue-500 text-blue-600'
            : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-900'}"
        >
          Dashboard
        </a>
        <a
          href="/products"
          class="inline-flex items-center border-b-2 text-sm font-medium transition-colors {$page.url.pathname.startsWith('/products')
            ? 'border-blue-500 text-blue-600'
            : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-900'}"
        >
          Products
        </a>

        <!-- Admin nav links - authenticated only -->
        {#if auth.isAuthenticated}
          <a
            href="/orders"
            class="inline-flex items-center border-b-2 text-sm font-medium transition-colors {$page.url.pathname.startsWith('/orders')
              ? 'border-blue-500 text-blue-600'
              : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-900'}"
          >
            Orders
          </a>
          <a
            href="/reports"
            class="inline-flex items-center border-b-2 text-sm font-medium transition-colors {$page.url.pathname.startsWith('/reports')
              ? 'border-blue-500 text-blue-600'
              : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-900'}"
          >
            Reports
          </a>
        {/if}

        <a
          href="/scalar/v1"
          target="_blank"
          rel="noopener noreferrer"
          class="inline-flex items-center gap-1 border-b-2 border-transparent text-sm font-medium text-gray-500 hover:border-gray-300 hover:text-gray-900 transition-colors"
        >
          API Docs
          <svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
          </svg>
        </a>
      </nav>

      {#if auth.isAuthenticated && auth.user}
        <div class="flex items-center gap-3">
          <span class="text-sm text-gray-700">
            {auth.user.displayName}
            <span class="ml-1 inline-flex items-center rounded-full bg-blue-50 px-2 py-0.5 text-xs font-medium text-blue-700">
              {auth.user.role}
            </span>
          </span>
          <button
            onclick={() => auth.logout()}
            class="rounded-md bg-gray-100 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors"
          >
            Sign out
          </button>
        </div>
      {:else}
        <div class="flex items-center">
          <a
            href="/login"
            class="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
          >
            Sign in
          </a>
        </div>
      {/if}
    </div>
  </div>
</header>
