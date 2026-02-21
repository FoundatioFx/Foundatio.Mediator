import { api } from './client';
import type {
  DashboardReport,
  SalesReport,
  InventoryReport,
  CatalogSearchResult
} from '$lib/types/report';

export const reportsApi = {
  dashboard: () => api.getJSON<DashboardReport>('/api/reports'),

  sales: (startDate?: string, endDate?: string) => {
    const params = new URLSearchParams();
    if (startDate) params.set('startDate', startDate);
    if (endDate) params.set('endDate', endDate);
    const query = params.toString();
    return api.getJSON<SalesReport>(`/api/reports/get-sales-report${query ? `?${query}` : ''}`);
  },

  inventory: () => api.getJSON<InventoryReport>('/api/reports/get-inventory-report'),

  searchCatalog: (searchTerm: string) =>
    api.getJSON<CatalogSearchResult>(`/api/reports/search-catalog?searchTerm=${encodeURIComponent(searchTerm)}`)
};
