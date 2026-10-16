import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'warning' | 'info';

export interface ToastMessage {
  readonly id: number;
  readonly kind: ToastKind;
  readonly message: string;
}

const DEFAULT_DURATION_MS = 5000;

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly messages = signal<readonly ToastMessage[]>([]);
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();
  private nextId = 1;

  readonly toasts = this.messages.asReadonly();

  show(kind: ToastKind, message: string, durationMs = DEFAULT_DURATION_MS): number {
    const id = this.nextId++;
    this.messages.update((current) => [...current, { id, kind, message }]);
    if (durationMs > 0) {
      this.timers.set(
        id,
        setTimeout(() => this.dismiss(id), durationMs),
      );
    }
    return id;
  }

  success(message: string, durationMs?: number): number {
    return this.show('success', message, durationMs);
  }

  error(message: string, durationMs?: number): number {
    return this.show('error', message, durationMs);
  }

  warning(message: string, durationMs?: number): number {
    return this.show('warning', message, durationMs);
  }

  info(message: string, durationMs?: number): number {
    return this.show('info', message, durationMs);
  }

  dismiss(id: number): void {
    const timer = this.timers.get(id);
    if (timer !== undefined) {
      clearTimeout(timer);
      this.timers.delete(id);
    }
    this.messages.update((current) => current.filter((toast) => toast.id !== id));
  }
}
