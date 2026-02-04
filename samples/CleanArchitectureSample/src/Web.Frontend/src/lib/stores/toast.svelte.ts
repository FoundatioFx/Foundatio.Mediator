export type ToastType = 'success' | 'error' | 'warning' | 'info';

export interface Toast {
  id: string;
  message: string;
  type: ToastType;
}

class ToastStore {
  toasts = $state<Toast[]>([]);

  add(message: string, type: ToastType = 'info', duration: number = 5000) {
    const id = crypto.randomUUID();
    this.toasts.push({ id, message, type });

    if (duration > 0) {
      setTimeout(() => this.remove(id), duration);
    }

    return id;
  }

  remove(id: string) {
    this.toasts = this.toasts.filter((t) => t.id !== id);
  }

  success(message: string) {
    return this.add(message, 'success');
  }

  error(message: string) {
    return this.add(message, 'error');
  }

  warning(message: string) {
    return this.add(message, 'warning');
  }

  info(message: string) {
    return this.add(message, 'info');
  }
}

export const toast = new ToastStore();
