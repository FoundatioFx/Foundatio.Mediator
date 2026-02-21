import type { OrderStatus } from './order';
import type { ProductStatus } from './product';

export interface RecentOrder {
  orderId: string;
  customerId: string;
  amount: number;
  status: OrderStatus;
  createdAt: string;
}

export interface TopProduct {
  productId: string;
  name: string;
  price: number;
  stockQuantity: number;
  status: ProductStatus;
}

export interface DashboardReport {
  totalOrders: number;
  totalProducts: number;
  totalRevenue: number;
  lowStockProductCount: number;
  recentOrders: RecentOrder[];
  topProducts: TopProduct[];
}

export interface DailySales {
  date: string;
  orderCount: number;
  revenue: number;
}

export interface SalesReport {
  startDate: string;
  endDate: string;
  orderCount: number;
  totalRevenue: number;
  averageOrderValue: number;
  dailySales: DailySales[];
}

export interface LowStockProduct {
  productId: string;
  name: string;
  stockQuantity: number;
  reorderThreshold: number;
}

export interface InventoryReport {
  totalProducts: number;
  activeProducts: number;
  outOfStockProducts: number;
  lowStockProducts: number;
  totalInventoryValue: number;
  lowStockItems: LowStockProduct[];
}

export interface ProductSearchResult {
  productId: string;
  name: string;
  description: string;
  price: number;
  status: ProductStatus;
}

export interface OrderSearchResult {
  orderId: string;
  customerId: string;
  description: string;
  amount: number;
  status: OrderStatus;
}

export interface CatalogSearchResult {
  searchTerm: string;
  products: ProductSearchResult[];
  orders: OrderSearchResult[];
}
