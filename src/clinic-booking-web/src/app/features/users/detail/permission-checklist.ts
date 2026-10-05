import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { PermissionLabels } from '../permission-labels';

/**
 * A group of checkboxes, one per permission name (D59). It has no strings of its own: the label and the
 * description come from `PermissionLabels`; a name without a label is shown as the bare name (left to
 * right), so a permission added to the API is still assignable before its wording exists.
 */
@Component({
  selector: 'cb-permission-checklist',
  templateUrl: './permission-checklist.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PermissionChecklist {
  protected readonly labels = inject(PermissionLabels);

  readonly names = input.required<readonly string[]>();
  readonly selected = input.required<ReadonlySet<string>>();
  /** Makes the control ids unique on a page that has several lists. */
  readonly idPrefix = input.required<string>();
  readonly disabled = input(false);

  readonly toggled = output<string>();

  protected onChange(name: string): void {
    this.toggled.emit(name);
  }

  protected idOf(name: string): string {
    return `${this.idPrefix()}-${name.replaceAll('.', '-')}`;
  }
}
