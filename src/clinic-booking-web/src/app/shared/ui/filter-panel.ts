import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * A "Filter" button that opens a dropdown panel with a list's filter form (D64). The caller projects the
 * fields and keeps a draft: `opened` asks it to copy the URL's filters into the draft, `applied` to
 * navigate with the draft, `cleared` to reset the filters and navigate. Nothing applies until Apply.
 * The button shows how many filters are active and carries `aria-expanded`; Esc and a click outside close
 * the panel and give the focus back to the button. The panel opens under the button, on the inline-start
 * side on phones and the inline-end side from sm up, so it stays on screen at 360 px in both directions.
 */
@Component({
  selector: 'cb-filter-panel',
  imports: [TranslocoPipe],
  templateUrl: './filter-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'relative',
    '(document:click)': 'onDocumentClick($event)',
    '(keydown.escape)': 'onEscape($event)',
  },
})
export class FilterPanel {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** The number of filters in effect (from the URL, not the draft). */
  readonly activeCount = input.required<number>();
  /** A unique id for the panel, for `aria-controls`. */
  readonly panelId = input.required<string>();

  readonly opened = output<void>();
  readonly applied = output<void>();
  readonly cleared = output<void>();

  protected readonly open = signal(false);

  private readonly toggleButton = viewChild.required<ElementRef<HTMLButtonElement>>('toggleButton');
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');

  protected toggle(): void {
    if (this.open()) {
      this.close();
      return;
    }
    this.opened.emit();
    this.open.set(true);
    afterNextRender(
      () => this.panel()?.nativeElement.querySelector<HTMLElement>('select, input, button')?.focus(),
      { injector: this.injector },
    );
  }

  protected apply(): void {
    this.applied.emit();
    this.close();
  }

  protected clear(): void {
    this.cleared.emit();
    this.close();
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node | null)) {
      this.close();
    }
  }

  protected onEscape(event: Event): void {
    if (this.open()) {
      event.preventDefault();
      this.close();
    }
  }

  private close(): void {
    this.open.set(false);
    this.toggleButton().nativeElement.focus();
  }
}
