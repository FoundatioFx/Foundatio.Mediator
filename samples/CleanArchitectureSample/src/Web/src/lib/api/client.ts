import { FetchClient } from '@foundatiofx/fetchclient';
import { tenant } from '$lib/stores/tenant.svelte';

// Always use relative URLs so the Vite dev-server proxy (or ASP.NET Core in production) handles routing.
export const api = new FetchClient({
  baseUrl: ''
});

// The API's TenantHeaderProvider copies this header into every queued message and tracked job.
api.use(async (context, next) => {
  context.request.headers.set('X-Tenant', tenant.current);
  await next();
});
