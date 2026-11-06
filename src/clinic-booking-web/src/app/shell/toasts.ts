import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { ToastService } from '../core/ui/toast.service';
import { Icon } from '../shared/ui/icon';

/**
 * The toast stack (D64), at the bottom on the inline-end side (mirrors in RTL). The polite status region
 * is always in the DOM, so a new toast is announced; a toast never takes the focus. Hovering a toast or
 * focusing its Close button pauses its countdown.
 */
@Component({
  selector: 'cb-toasts',
  imports: [TranslocoPipe, Icon],
  templateUrl: './toasts.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Toasts {
  protected readonly service = inject(ToastService);

  private readonly hovered = new Set<number>();
  private readonly focused = new Set<number>();

  protected hold(id: number, reason: 'hover' | 'focus'): void {
    (reason === 'hover' ? this.hovered : this.focused).add(id);
    this.service.pause(id);
  }

  protected release(id: number, reason: 'hover' | 'focus', event?: FocusEvent): void {
    // Focus moving inside the same toast is not a release.
    if (reason === 'focus' && event !== undefined && (event.currentTarget as HTMLElement).contains(event.relatedTarget as Node | null)) {
      return;
    }
    (reason === 'hover' ? this.hovered : this.focused).delete(id);
    if (!this.hovered.has(id) && !this.focused.has(id)) {
      this.service.resume(id);
    }
  }

  protected close(id: number): void {
    this.hovered.delete(id);
    this.focused.delete(id);
    this.service.dismiss(id);
  }
}
