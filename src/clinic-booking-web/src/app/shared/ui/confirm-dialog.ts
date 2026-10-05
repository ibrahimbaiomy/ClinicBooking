import {
  ChangeDetectionStrategy,
  Component,
  effect,
  ElementRef,
  input,
  output,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * A confirmation dialog on the native `<dialog>` (D53): `showModal()` gives the focus trap, the
 * inert background, Esc to close and focus restoration to the opener. Focus starts on Cancel.
 * For an input dialog (D59) the caller passes `focusSelector`, the control inside the dialog that should
 * take the focus instead of Cancel.
 * The caller owns the state (`open`) and projects every text, so the component has no strings:
 * `[cbDialogTitle]`, the body (default slot), `[cbDialogCancel]` and `[cbDialogConfirm]`.
 */
@Component({
  selector: 'cb-confirm-dialog',
  imports: [TranslocoPipe],
  templateUrl: './confirm-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmDialog {
  readonly open = input(false);
  /** A CSS selector for the control that takes the initial focus; absent means Cancel (a confirmation). */
  readonly focusSelector = input<string | null>(null);
  /** True while the confirmed action runs: both buttons and Esc are disabled. */
  readonly busy = input(false);
  /** A translation key for an error to show inside the dialog (already resolved by the caller). */
  readonly errorKey = input<string | null>(null);

  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly cancelButton = viewChild.required<ElementRef<HTMLButtonElement>>('cancelButton');

  constructor() {
    effect(() => {
      const element = this.dialog().nativeElement;
      if (this.open() && !element.open) {
        element.showModal();
        const selector = this.focusSelector();
        const target = selector === null ? null : element.querySelector<HTMLElement>(selector);
        (target ?? this.cancelButton().nativeElement).focus();
      } else if (!this.open() && element.open) {
        element.close();
      }
    });
  }

  /** Esc: the parent decides (it closes by setting `open` to false); never while busy. */
  protected onNativeCancel(event: Event): void {
    event.preventDefault();
    if (!this.busy()) {
      this.cancelled.emit();
    }
  }

  /** The dialog closed by some other route while the parent still thinks it is open. */
  protected onNativeClose(): void {
    if (this.open()) {
      this.cancelled.emit();
    }
  }
}
