export type OrderCreatedEvent = { orderId: string; customerId: string; amount: number; createdAt: string };
export type OrderUpdatedEvent = { orderId: string; amount: number; status: string; updatedAt: string };
export type OrderShippedEvent = { orderId: string; carrier: string; trackingNumber: string; shippedAt: string };
export type OrderDeletedEvent = { orderId: string; deletedAt: string };
export type ProductCreatedEvent = { productId: string; name: string; price: number; createdAt: string };
export type ProductUpdatedEvent = { productId: string; name: string; price: number; status: string; updatedAt: string };
export type ProductDeletedEvent = { productId: string; deletedAt: string };
export type DemoJobCompletedEvent = { jobId: string; queueName: string; hostId: string; tenant: string };

export type ClientEvent = {
  eventType: string;
  data: Record<string, unknown>;
};

export type EventCategory = 'order' | 'product' | 'job' | 'other';

export type EventEntry = {
  id: number;
  timestamp: Date;
  type: string;
  category: EventCategory;
  /** The trailing word of the event name: created, shipped, completed, ... */
  action: string;
  /** The worker or API process that published the event, when the event carries one. */
  host: string | null;
  data: Record<string, unknown>;
};

type Listener<T> = (event: T) => void;

/**
 * SSE-based event service. Connects to the server's /api/events endpoint with EventSource and dispatches
 * every IDispatchToClient notification to listeners registered by event type. Events are buffered globally
 * so the Live Events page shows what happened while it was not visible.
 */
class EventStreamService {
  private eventSource: EventSource | null = null;
  private listeners = new Map<string, Listener<Record<string, unknown>>[]>();
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private nextId = 0;
  private maxEvents = 300;

  isConnected = $state(false);
  events = $state<EventEntry[]>([]);
  paused = $state(false);

  clearEvents() {
    this.events = [];
  }

  start() {
    if (this.eventSource) return;
    this.connect();
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

  /** Listen for one event type by name, e.g. `on<DemoJobCompletedEvent>('DemoJobCompleted', ...)`. */
  on<T extends Record<string, unknown>>(type: string, callback: Listener<T>): () => void {
    const list = this.listeners.get(type) ?? [];
    list.push(callback as Listener<Record<string, unknown>>);
    this.listeners.set(type, list);
    return () => {
      this.listeners.set(type, (this.listeners.get(type) ?? []).filter((cb) => cb !== callback));
    };
  }

  onOrderCreated(callback: Listener<OrderCreatedEvent>) { return this.on('OrderCreated', callback); }
  onOrderUpdated(callback: Listener<OrderUpdatedEvent>) { return this.on('OrderUpdated', callback); }
  onOrderShipped(callback: Listener<OrderShippedEvent>) { return this.on('OrderShipped', callback); }
  onOrderDeleted(callback: Listener<OrderDeletedEvent>) { return this.on('OrderDeleted', callback); }
  onProductCreated(callback: Listener<ProductCreatedEvent>) { return this.on('ProductCreated', callback); }
  onProductUpdated(callback: Listener<ProductUpdatedEvent>) { return this.on('ProductUpdated', callback); }
  onProductDeleted(callback: Listener<ProductDeletedEvent>) { return this.on('ProductDeleted', callback); }

  private connect() {
    this.eventSource = new EventSource('/api/events');

    this.eventSource.addEventListener('message', (e: MessageEvent) => {
      try {
        this.dispatch(JSON.parse(e.data) as ClientEvent);
      } catch (err) {
        console.error('Failed to parse SSE event:', err);
      }
    });

    this.eventSource.onopen = () => {
      this.isConnected = true;
      if (this.reconnectTimer) {
        clearTimeout(this.reconnectTimer);
        this.reconnectTimer = null;
      }
    };

    this.eventSource.onerror = () => {
      this.isConnected = false;
      if (this.eventSource?.readyState === EventSource.CLOSED) {
        this.eventSource = null;
        this.reconnectTimer = setTimeout(() => this.connect(), 3000);
      }
    };
  }

  private dispatch({ eventType, data }: ClientEvent) {
    if (!this.paused) {
      const entry: EventEntry = {
        id: this.nextId++,
        timestamp: new Date(),
        type: eventType,
        category: categorize(eventType),
        action: actionOf(eventType),
        host: typeof data.hostId === 'string' ? data.hostId : null,
        data
      };
      this.events = [entry, ...this.events].slice(0, this.maxEvents);
    }

    this.listeners.get(eventType)?.forEach((cb) => cb(data));
  }
}

function categorize(type: string): EventCategory {
  if (type.startsWith('Order')) return 'order';
  if (type.startsWith('Product')) return 'product';
  if (type.startsWith('DemoJob') || type.startsWith('BankFile') || type.startsWith('Webhook')) return 'job';
  return 'other';
}

function actionOf(type: string): string {
  const match = /([A-Z][a-z]+)$/.exec(type);
  return match ? match[1].toLowerCase() : 'event';
}

export const eventStream = new EventStreamService();
