const STORAGE_KEY = 'sample.tenant';

export const TENANTS = ['acme', 'globex', 'initech'] as const;

/**
 * The tenant every API request is made on behalf of. It travels as the X-Tenant header, and the API copies
 * it into queued messages and tracked-job metadata, so the worker logs and the queue dashboard show it.
 */
class TenantStore {
  current = $state<string>(TENANTS[0]);

  constructor() {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved) this.current = saved;
    } catch {
      // storage unavailable; keep the default
    }
  }

  set(value: string) {
    this.current = value;
    try {
      localStorage.setItem(STORAGE_KEY, value);
    } catch {
      // storage unavailable; the in-memory value still applies
    }
  }
}

export const tenant = new TenantStore();
