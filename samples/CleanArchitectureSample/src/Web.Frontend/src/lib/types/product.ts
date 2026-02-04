export type ProductStatus = 'Draft' | 'Active' | 'OutOfStock' | 'Discontinued';

export interface Product {
  id: string;
  name: string;
  description: string;
  price: number;
  stockQuantity: number;
  status: ProductStatus;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateProductRequest {
  name: string;
  description: string;
  price: number;
  stockQuantity?: number;
}

export interface UpdateProductRequest {
  name?: string;
  description?: string;
  price?: number;
  stockQuantity?: number;
  status?: ProductStatus;
}

export const PRODUCT_STATUS_COLORS: Record<ProductStatus, string> = {
  Draft: 'bg-gray-100 text-gray-800',
  Active: 'bg-green-100 text-green-800',
  OutOfStock: 'bg-red-100 text-red-800',
  Discontinued: 'bg-orange-100 text-orange-800'
};
