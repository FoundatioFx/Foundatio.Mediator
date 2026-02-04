import { api } from './client';
import type { Product, CreateProductRequest, UpdateProductRequest } from '$lib/types/product';

export const productsApi = {
  list: () => api.getJSON<Product[]>('/api/products'),

  get: (id: string) => api.getJSON<Product>(`/api/products/${id}`),

  create: (data: CreateProductRequest) => api.postJSON<Product>('/api/products', data),

  update: (id: string, data: UpdateProductRequest) =>
    api.putJSON<Product>(`/api/products/${id}`, data),

  delete: (id: string) => api.deleteJSON<void>(`/api/products/${id}`)
};
