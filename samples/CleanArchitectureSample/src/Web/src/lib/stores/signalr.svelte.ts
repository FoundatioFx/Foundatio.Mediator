import * as signalR from '@microsoft/signalr';

// Event types that the server can send
export type OrderCreatedEvent = {
  orderId: string;
  customerId: string;
  amount: number;
  createdAt: string;
};

export type OrderUpdatedEvent = {
  orderId: string;
  amount: number;
  status: string;
  updatedAt: string;
};

export type OrderDeletedEvent = {
  orderId: string;
  deletedAt: string;
};

export type ProductCreatedEvent = {
  productId: string;
  name: string;
  price: number;
  createdAt: string;
};

export type ProductUpdatedEvent = {
  productId: string;
  name: string;
  price: number;
  status: string;
  updatedAt: string;
};

export type ProductDeletedEvent = {
  productId: string;
  deletedAt: string;
};

type EventCallback<T> = (event: T) => void;

class SignalRService {
  private connection: signalR.HubConnection | null = null;
  private orderCreatedCallbacks: EventCallback<OrderCreatedEvent>[] = [];
  private orderUpdatedCallbacks: EventCallback<OrderUpdatedEvent>[] = [];
  private orderDeletedCallbacks: EventCallback<OrderDeletedEvent>[] = [];
  private productCreatedCallbacks: EventCallback<ProductCreatedEvent>[] = [];
  private productUpdatedCallbacks: EventCallback<ProductUpdatedEvent>[] = [];
  private productDeletedCallbacks: EventCallback<ProductDeletedEvent>[] = [];

  isConnected = $state(false);

  async start() {
    if (this.connection) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/events')
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Register event handlers
    this.connection.on('OrderCreated', (event: OrderCreatedEvent) => {
      console.log('OrderCreated event received:', event);
      this.orderCreatedCallbacks.forEach((cb) => cb(event));
    });

    this.connection.on('OrderUpdated', (event: OrderUpdatedEvent) => {
      console.log('OrderUpdated event received:', event);
      this.orderUpdatedCallbacks.forEach((cb) => cb(event));
    });

    this.connection.on('OrderDeleted', (event: OrderDeletedEvent) => {
      console.log('OrderDeleted event received:', event);
      this.orderDeletedCallbacks.forEach((cb) => cb(event));
    });

    this.connection.on('ProductCreated', (event: ProductCreatedEvent) => {
      console.log('ProductCreated event received:', event);
      this.productCreatedCallbacks.forEach((cb) => cb(event));
    });

    this.connection.on('ProductUpdated', (event: ProductUpdatedEvent) => {
      console.log('ProductUpdated event received:', event);
      this.productUpdatedCallbacks.forEach((cb) => cb(event));
    });

    this.connection.on('ProductDeleted', (event: ProductDeletedEvent) => {
      console.log('ProductDeleted event received:', event);
      this.productDeletedCallbacks.forEach((cb) => cb(event));
    });

    this.connection.onclose(() => {
      this.isConnected = false;
    });

    this.connection.onreconnected(() => {
      this.isConnected = true;
    });

    try {
      await this.connection.start();
      this.isConnected = true;
      console.log('SignalR connected');
    } catch (err) {
      console.error('SignalR connection error:', err);
    }
  }

  async stop() {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
      this.isConnected = false;
    }
  }

  // Order event subscriptions
  onOrderCreated(callback: EventCallback<OrderCreatedEvent>) {
    this.orderCreatedCallbacks.push(callback);
    return () => {
      this.orderCreatedCallbacks = this.orderCreatedCallbacks.filter((cb) => cb !== callback);
    };
  }

  onOrderUpdated(callback: EventCallback<OrderUpdatedEvent>) {
    this.orderUpdatedCallbacks.push(callback);
    return () => {
      this.orderUpdatedCallbacks = this.orderUpdatedCallbacks.filter((cb) => cb !== callback);
    };
  }

  onOrderDeleted(callback: EventCallback<OrderDeletedEvent>) {
    this.orderDeletedCallbacks.push(callback);
    return () => {
      this.orderDeletedCallbacks = this.orderDeletedCallbacks.filter((cb) => cb !== callback);
    };
  }

  // Product event subscriptions
  onProductCreated(callback: EventCallback<ProductCreatedEvent>) {
    this.productCreatedCallbacks.push(callback);
    return () => {
      this.productCreatedCallbacks = this.productCreatedCallbacks.filter((cb) => cb !== callback);
    };
  }

  onProductUpdated(callback: EventCallback<ProductUpdatedEvent>) {
    this.productUpdatedCallbacks.push(callback);
    return () => {
      this.productUpdatedCallbacks = this.productUpdatedCallbacks.filter((cb) => cb !== callback);
    };
  }

  onProductDeleted(callback: EventCallback<ProductDeletedEvent>) {
    this.productDeletedCallbacks.push(callback);
    return () => {
      this.productDeletedCallbacks = this.productDeletedCallbacks.filter((cb) => cb !== callback);
    };
  }
}

export const signalr = new SignalRService();
