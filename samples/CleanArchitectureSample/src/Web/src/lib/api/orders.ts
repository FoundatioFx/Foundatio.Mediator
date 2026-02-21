import { api } from './client';
import type { Order, CreateOrderRequest, UpdateOrderRequest } from '$lib/types/order';

export const ordersApi = {
  list: () => api.getJSON<Order[]>('/api/orders'),

  get: (id: string) => api.getJSON<Order>(`/api/orders/${id}`),

  create: (data: CreateOrderRequest) => api.postJSON<Order>('/api/orders', data),

  update: (id: string, data: UpdateOrderRequest) =>
    api.putJSON<Order>(`/api/orders/${id}`, data),

  delete: (id: string) => api.deleteJSON<void>(`/api/orders/${id}`)
};
