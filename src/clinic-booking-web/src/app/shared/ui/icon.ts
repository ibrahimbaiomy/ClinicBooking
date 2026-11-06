import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ICON_REGISTRY } from './icons';

/**
 * One Bootstrap icon (D65), always decorative: `aria-hidden`, never focusable; the text next to it is the
 * accessible name. Sized 1em by default so it follows the text. A directional icon is mirrored in RTL with
 * `rtl:-scale-x-100` (D25).
 *
 * The markup is trusted on purpose: it comes only from the build-time registry (`icons.ts`), never from
 * the server or a user, and Angular's sanitizer would otherwise strip SVG.
 */
@Component({
  selector: 'cb-icon',
  templateUrl: './icon.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'aria-hidden': 'true',
    '[class]': 'classes()',
    '[attr.data-icon]': 'name()',
    '[innerHTML]': 'markup()',
  },
})
export class Icon {
  private readonly registry = inject(ICON_REGISTRY);
  private readonly sanitizer = inject(DomSanitizer);

  readonly name = input.required<string>();

  protected readonly directional = computed(() => this.registry[this.name()]?.directional ?? false);

  protected readonly classes = computed(
    () => 'inline-flex size-[1em] shrink-0 [&>svg]:size-full' + (this.directional() ? ' rtl:-scale-x-100' : ''),
  );

  protected readonly markup = computed<SafeHtml>(() => {
    const svg = (this.registry[this.name()]?.svg ?? '').replace('<svg ', '<svg aria-hidden="true" focusable="false" ');
    return this.sanitizer.bypassSecurityTrustHtml(svg);
  });
}
