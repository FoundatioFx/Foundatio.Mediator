<script lang="ts">
  import { page } from '$app/stores';
</script>

<header class="bg-white shadow-sm border-b border-gray-200">
  <div class="px-4 sm:px-6 lg:px-8">
    <div class="flex h-16 justify-between items-center">
      <div class="flex items-center">
        <a href="/" class="text-xl font-bold text-gray-900">
          Clean Architecture
        </a>
      </div>

      <nav class="hidden md:flex space-x-8">
        <a
          href="/"
          class="text-sm font-medium transition-colors {$page.url.pathname === '/'
            ? 'text-blue-600'
            : 'text-gray-500 hover:text-gray-900'}"
        >
          Dashboard
        </a>
        <a
          href="/orders"
          class="text-sm font-medium transition-colors {$page.url.pathname.startsWith('/orders')
            ? 'text-blue-600'
            : 'text-gray-500 hover:text-gray-900'}"
        >
          Orders
        </a>
        <a
          href="/products"
          class="text-sm font-medium transition-colors {$page.url.pathname.startsWith('/products')
            ? 'text-blue-600'
            : 'text-gray-500 hover:text-gray-900'}"
        >
          Products
        </a>
        <a
          href="/reports"
          class="text-sm font-medium transition-colors {$page.url.pathname.startsWith('/reports')
            ? 'text-blue-600'
            : 'text-gray-500 hover:text-gray-900'}"
        >
          Reports
        </a>
        <a
          href="/scalar/v1"
          target="_blank"
          rel="noopener noreferrer"
          class="text-sm font-medium text-gray-500 hover:text-gray-900 transition-colors flex items-center gap-1"
        >
          API Docs
          <svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
          </svg>
        </a>
      </nav>
    </div>
  </div>
</header>
