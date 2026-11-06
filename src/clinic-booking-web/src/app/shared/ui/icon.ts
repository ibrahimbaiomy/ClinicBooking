import { ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, input } from '@angular/core';
import { ICON_REGISTRY } from './icons';

/**
 * One Bootstrap icon (D65), always decorative: `aria-hidden`, never focusable; the text next to it is the
 * accessible name. Sized 1em by default so it follows the text. A directional icon is mirrored in RTL with
 * `rtl:-scale-x-100` (D25).
 *
 * The markup is written to the element directly: it comes only from the build-time registry (`icons.ts`),
 * never from the server or a user. An `[innerHTML]` binding would strip the SVG and pull Angular's HTML
 * sanitizer into the bundle (about 10 kB) for nothing.
 */
@Component({
  selector: 'cb-icon',
  templateUrl: './icon.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'aria-hidden': 'true',
    '[class]': 'classes()',
    '[attr.data-icon]': 'name()',
  },
})
export class Icon {
  private readonly registry = inject(ICON_REGISTRY);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly name = input.required<string>();

  protected readonly directional = computed(() => this.registry[this.name()]?.directional ?? false);

  protected readonly classes = computed(
    () => 'inline-flex size-[1em] shrink-0 [&>svg]:size-full' + (this.directional() ? ' rtl:-scale-x-100' : ''),
  );

  private readonly markup = computed(() =>
    (this.registry[this.name()]?.svg ?? '').replace('<svg ', '<svg aria-hidden="true" focusable="false" '),
  );

  constructor() {
    effect(() => {
      this.host.nativeElement.innerHTML = this.markup();
    });
  }
}
