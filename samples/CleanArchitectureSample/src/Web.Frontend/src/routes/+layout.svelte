<script lang="ts">
  import '../app.css';
  import { Header, Sidebar, ToastContainer } from '$lib/components/layout';

  let { children } = $props();
</script>

<div class="min-h-screen bg-gray-50">
  <Header />
  <div class="flex">
    <Sidebar />
    <main class="flex-1 p-6">
      {@render children()}
    </main>
  </div>
  <ToastContainer />
</div>
