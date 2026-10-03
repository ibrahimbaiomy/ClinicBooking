import { Directive, effect, inject, input, TemplateRef, ViewContainerRef } from '@angular/core';
import { SessionService } from './session.service';

/**
 * `<button *cbCan="'specialties.manage'">`: shows the element only when the user has the
 * permission. UX only; the API enforces permissions on every call.
 */
@Directive({ selector: '[cbCan]' })
export class CanDirective {
  private readonly session = inject(SessionService);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);

  readonly cbCan = input.required<string>();

  constructor() {
    let shown = false;
    effect(() => {
      const allowed = this.session.can(this.cbCan());
      if (allowed && !shown) {
        this.container.createEmbeddedView(this.template);
      } else if (!allowed && shown) {
        this.container.clear();
      }
      shown = allowed;
    });
  }
}
