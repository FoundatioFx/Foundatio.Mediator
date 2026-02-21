export type OrderStatus =
  | 'Pending'
  | 'Confirmed'
  | 'Processing'
  | 'Shipped'
  | 'Delivered'
  | 'Cancelled';

export interface Order {
  id: string;
  customerId: string;
  amount: number;
  description: string;
  status: OrderStatus;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateOrderRequest {
  customerId: string;
  amount: number;
  description: string;
}

export interface UpdateOrderRequest {
  amount?: number;
  description?: string;
  status?: OrderStatus;
}

export const ORDER_STATUS_COLORS: Record<OrderStatus, string> = {
  Pending: 'bg-yellow-100 text-yellow-800',
  Confirmed: 'bg-blue-100 text-blue-800',
  Processing: 'bg-purple-100 text-purple-800',
  Shipped: 'bg-indigo-100 text-indigo-800',
  Delivered: 'bg-green-100 text-green-800',
  Cancelled: 'bg-red-100 text-red-800'
};
