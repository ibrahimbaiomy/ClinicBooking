import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { PermissionLabels } from '../permission-labels';

/** A checkbox the user changed: its name, the new state, and the element (to undo a change the page refuses). */
export interface PermissionToggle {
  name: string;
  checked: boolean;
  input: HTMLInputElement;
}

/**
 * A group of checkboxes, one per permission name (D59), saved on each change (D64). Labels come from
 * `PermissionLabels`; a name without a label is shown as the bare name (left to right), so a permission
 * added to the API is still assignable before its wording exists. Next to each item: "Saved" for a while
 * after its request succeeded, or its translated error. A pending or locked item is disabled.
 */
@Component({
  selector: 'cb-permission-checklist',
  imports: [TranslocoPipe],
  templateUrl: './permission-checklist.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PermissionChecklist {
  protected readonly labels = inject(PermissionLabels);

  readonly names = input.required<readonly string[]>();
  readonly selected = input.required<ReadonlySet<string>>();
  /** Makes the control ids unique on a page that has several lists. */
  readonly idPrefix = input.required<string>();
  /** Names whose request is queued or in flight. */
  readonly pending = input<ReadonlySet<string>>(new Set());
  /** Names that cannot change here (the protected administrator's users.manage), with the hint key. */
  readonly locked = input<Readonly<Record<string, string>>>({});
  /** Names saved recently. */
  readonly saved = input<ReadonlySet<string>>(new Set());
  /** Translated error keys, by name. */
  readonly errors = input<Readonly<Record<string, string>>>({});

  readonly toggled = output<PermissionToggle>();

  protected onChange(name: string, event: Event): void {
    const element = event.target as HTMLInputElement;
    this.toggled.emit({ name, checked: element.checked, input: element });
  }

  protected idOf(name: string): string {
    return `${this.idPrefix()}-${name.replaceAll('.', '-')}`;
  }

  protected describedBy(name: string): string | null {
    const ids = [
      this.labels.labelOf(name) ? `${this.idOf(name)}-description` : null,
      this.locked()[name] !== undefined ? `${this.idOf(name)}-locked` : null,
      `${this.idOf(name)}-state`,
    ].filter((id) => id !== null);
    return ids.join(' ');
  }
}
