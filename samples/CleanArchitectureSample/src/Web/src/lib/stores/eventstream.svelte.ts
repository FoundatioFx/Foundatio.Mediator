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

export type ClientEvent = {
  eventType: string;
  data: Record<string, unknown>;
};

export type EventEntry = {
  id: number;
  timestamp: Date;
  type: string;
  category: 'order' | 'product';
  action: 'created' | 'updated' | 'deleted';
  data: Record<string, unknown>;
};

type EventCallback<T> = (event: T) => void;

/**
 * SSE-based event service. Connects to the server's /events/stream endpoint
 * using the EventSource API and dispatches domain events to registered callbacks.
 * Buffers events globally so they are captured even when the events page is not visible.
 */
class EventStreamService {
  private eventSource: EventSource | null = null;
  private orderCreatedCallbacks: EventCallback<OrderCreatedEvent>[] = [];
  private orderUpdatedCallbacks: EventCallback<OrderUpdatedEvent>[] = [];
  private orderDeletedCallbacks: EventCallback<OrderDeletedEvent>[] = [];
  private productCreatedCallbacks: EventCallback<ProductCreatedEvent>[] = [];
  private productUpdatedCallbacks: EventCallback<ProductUpdatedEvent>[] = [];
  private productDeletedCallbacks: EventCallback<ProductDeletedEvent>[] = [];
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private nextId = 0;
  private maxEvents = 200;

  isConnected = $state(false);
  events = $state<EventEntry[]>([]);
  paused = $state(false);

  private addEvent(type: string, category: EventEntry['category'], action: EventEntry['action'], data: Record<string, unknown>) {
    if (this.paused) return;
    const entry: EventEntry = { id: this.nextId++, timestamp: new Date(), type, category, action, data };
    this.events = [entry, ...this.events].slice(0, this.maxEvents);
  }

  clearEvents() {
    this.events = [];
  }

  start() {
    if (this.eventSource) return;
    this.connect();
  }

  private connect() {
    this.eventSource = new EventSource('/api/events');

    this.eventSource.addEventListener('message', (e: MessageEvent) => {
      try {
        const clientEvent: ClientEvent = JSON.parse(e.data);
        this.dispatch(clientEvent);
      } catch (err) {
        console.error('Failed to parse SSE event:', err);
      }
    });

    this.eventSource.onopen = () => {
      this.isConnected = true;
      console.log('SSE connected');
      if (this.reconnectTimer) {
        clearTimeout(this.reconnectTimer);
        this.reconnectTimer = null;
      }
    };

    this.eventSource.onerror = () => {
      this.isConnected = false;
      console.warn('SSE connection error, will auto-reconnect...');
      // EventSource automatically reconnects, but if it closes we handle it
      if (this.eventSource?.readyState === EventSource.CLOSED) {
        this.eventSource = null;
        // Reconnect after a delay
        this.reconnectTimer = setTimeout(() => this.connect(), 3000);
      }
    };
  }

  private dispatch(clientEvent: ClientEvent) {
    const { eventType, data } = clientEvent;
    console.log(`${eventType} event received:`, data);

    switch (eventType) {
      case 'OrderCreated':
        this.addEvent('OrderCreated', 'order', 'created', { orderId: (data as unknown as OrderCreatedEvent).orderId, customerId: (data as unknown as OrderCreatedEvent).customerId, amount: (data as unknown as OrderCreatedEvent).amount });
        this.orderCreatedCallbacks.forEach((cb) => cb(data as unknown as OrderCreatedEvent));
        break;
      case 'OrderUpdated':
        this.addEvent('OrderUpdated', 'order', 'updated', { orderId: (data as unknown as OrderUpdatedEvent).orderId, amount: (data as unknown as OrderUpdatedEvent).amount, status: (data as unknown as OrderUpdatedEvent).status });
        this.orderUpdatedCallbacks.forEach((cb) => cb(data as unknown as OrderUpdatedEvent));
        break;
      case 'OrderDeleted':
        this.addEvent('OrderDeleted', 'order', 'deleted', { orderId: (data as unknown as OrderDeletedEvent).orderId });
        this.orderDeletedCallbacks.forEach((cb) => cb(data as unknown as OrderDeletedEvent));
        break;
      case 'ProductCreated':
        this.addEvent('ProductCreated', 'product', 'created', { productId: (data as unknown as ProductCreatedEvent).productId, name: (data as unknown as ProductCreatedEvent).name, price: (data as unknown as ProductCreatedEvent).price });
        this.productCreatedCallbacks.forEach((cb) => cb(data as unknown as ProductCreatedEvent));
        break;
      case 'ProductUpdated':
        this.addEvent('ProductUpdated', 'product', 'updated', { productId: (data as unknown as ProductUpdatedEvent).productId, name: (data as unknown as ProductUpdatedEvent).name, price: (data as unknown as ProductUpdatedEvent).price, status: (data as unknown as ProductUpdatedEvent).status });
        this.productUpdatedCallbacks.forEach((cb) => cb(data as unknown as ProductUpdatedEvent));
        break;
      case 'ProductDeleted':
        this.addEvent('ProductDeleted', 'product', 'deleted', { productId: (data as unknown as ProductDeletedEvent).productId });
        this.productDeletedCallbacks.forEach((cb) => cb(data as unknown as ProductDeletedEvent));
        break;
    }
  }

  stop() {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    if (this.eventSource) {
      this.eventSource.close();
      this.eventSource = null;
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

export const eventStream = new EventStreamService();
