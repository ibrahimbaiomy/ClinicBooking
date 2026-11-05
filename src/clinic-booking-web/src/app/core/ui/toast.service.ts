import { inject, Injectable, signal } from '@angular/core';
import { MESSAGE_DURATION_MS } from '../../app-settings';

export type ToastParams = Record<string, string | number>;

export interface Toast {
  id: number;
  /** A translation key; resolved when shown, so it follows a language change. */
  key: string;
  params: ToastParams | undefined;
}

interface Timer {
  handle: ReturnType<typeof setTimeout> | null;
  remaining: number;
  startedAt: number;
}

/**
 * One-time success messages (D64): shown by the shell, they outlive a navigation, disappear after the
 * configured duration, pause while hovered or focused and can be closed. Errors never go here: they stay
 * next to what failed. Keys passed to `showToast('…')` as literals are verified by check:i18n.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly duration = inject(MESSAGE_DURATION_MS);
  private readonly list = signal<readonly Toast[]>([]);
  private readonly timers = new Map<number, Timer>();
  private nextId = 1;

  readonly toasts = this.list.asReadonly();

  showToast(key: string, params?: ToastParams): void {
    const id = this.nextId++;
    this.list.update((toasts) => [...toasts, { id, key, params }]);
    this.timers.set(id, { handle: null, remaining: this.duration, startedAt: 0 });
    this.run(id);
  }

  dismiss(id: number): void {
    const timer = this.timers.get(id);
    if (timer?.handle != null) clearTimeout(timer.handle);
    this.timers.delete(id);
    this.list.update((toasts) => toasts.filter((toast) => toast.id !== id));
  }

  /** Stops the countdown, keeping the time left. Idempotent. */
  pause(id: number): void {
    const timer = this.timers.get(id);
    if (timer === undefined || timer.handle === null) return;
    clearTimeout(timer.handle);
    timer.handle = null;
    timer.remaining = Math.max(0, timer.remaining - (Date.now() - timer.startedAt));
  }

  /** Continues the countdown with the time that was left. Idempotent. */
  resume(id: number): void {
    const timer = this.timers.get(id);
    if (timer === undefined || timer.handle !== null) return;
    this.run(id);
  }

  private run(id: number): void {
    const timer = this.timers.get(id);
    if (timer === undefined) return;
    timer.startedAt = Date.now();
    timer.handle = setTimeout(() => this.dismiss(id), timer.remaining);
  }
}
