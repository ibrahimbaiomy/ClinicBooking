import { Directive, effect, inject, input, TemplateRef, ViewContainerRef } from '@angular/core';
import { SessionService } from './session.service';

/**
 * `<button *cbCan="'specialties.manage'">`: shows the element only when the user has the global
 * permission. `<button *cbCan="'doctors.manage'; clinic: clinicId">`: shows it only when the user holds
 * the clinic-scoped permission in that clinic (D57, D59); a global permission never satisfies it.
 * UX only; the API enforces permissions on every call.
 */
@Directive({ selector: '[cbCan]' })
export class CanDirective {
  private readonly session = inject(SessionService);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);

  readonly cbCan = input.required<string>();
  /** The clinic the permission is asked in; absent means a global permission. */
  readonly cbCanClinic = input<number | string | null>(null);

  constructor() {
    let shown = false;
    effect(() => {
      const clinic = this.cbCanClinic();
      const allowed =
        clinic === null ? this.session.can(this.cbCan()) : this.session.canIn(this.cbCan(), clinic);
      if (allowed && !shown) {
        this.container.createEmbeddedView(this.template);
      } else if (!allowed && shown) {
        this.container.clear();
      }
      shown = allowed;
    });
  }
}
