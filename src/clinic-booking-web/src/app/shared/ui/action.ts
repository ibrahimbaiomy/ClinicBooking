import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Icon } from './icon';
import { IconName } from './icons';

/** The action-button kinds (D65). Every other button keeps its own look. */
export type ActionKind = 'add' | 'edit' | 'save-new' | 'save-existing' | 'delete' | 'danger' | 'cancel';

const BASE =
  'inline-flex items-center justify-center gap-1.5 rounded-lg border bg-transparent px-3 py-1.5 text-sm font-medium ' +
  'transition-colors hover:text-white focus-visible:outline-2 focus-visible:outline-offset-2 ' +
  'disabled:pointer-events-none disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50';

// Text and border on white: green-700 ≈ 5.0:1, blue-700 ≈ 6.7:1, red-700 ≈ 6.5:1, slate-600 ≈ 7.6:1.
const GREEN = 'border-green-700 text-green-700 hover:bg-green-700 focus-visible:outline-green-700';
const BLUE = 'border-blue-700 text-blue-700 hover:bg-blue-700 focus-visible:outline-blue-700';
const RED = 'border-red-700 text-red-700 hover:bg-red-700 focus-visible:outline-red-700';
const GREY = 'border-slate-600 text-slate-600 hover:bg-slate-600 focus-visible:outline-slate-600';

/**
 * The one place the action colours and icons live (D65): Bootstrap-style outline buttons. Save is green on a
 * create form and blue on an edit form; "danger" is the red outline for the dialog confirmations other than
 * delete, each with its own icon passed as `cbActionIcon`.
 */
export const ACTION_STYLES: Readonly<Record<ActionKind, { classes: string; icon: IconName | null }>> = {
  add: { classes: GREEN, icon: 'plus' },
  edit: { classes: BLUE, icon: 'pencil' },
  'save-new': { classes: GREEN, icon: 'check' },
  'save-existing': { classes: BLUE, icon: 'check' },
  delete: { classes: RED, icon: 'trash' },
  danger: { classes: RED, icon: null },
  cancel: { classes: GREY, icon: 'x' },
};

/**
 * An action button or link: `<button cbAction="delete">…text…</button>` (D65). It adds the shared classes
 * and puts the kind's icon (decorative) before the projected text, which stays the accessible name. Layout
 * classes (margins, width) may still be added by the page; colours must not be.
 */
@Component({
  // An attribute on the native element keeps its semantics, keyboard and form behaviour (D65).
  // eslint-disable-next-line @angular-eslint/component-selector
  selector: 'button[cbAction], a[cbAction]',
  imports: [Icon],
  templateUrl: './action.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class]': 'classes()',
    '[attr.data-action]': 'kind()',
  },
})
export class Action {
  readonly kind = input.required<ActionKind>({ alias: 'cbAction' });
  /**
   * The icon of a `danger` action, which names what it does (pause-circle, person-slash, key, shield-slash);
   * the other kinds keep their own icon.
   */
  readonly cbActionIcon = input<IconName | null>(null);

  protected readonly icon = computed(() => this.cbActionIcon() ?? ACTION_STYLES[this.kind()].icon);
  protected readonly classes = computed(() => `${BASE} ${ACTION_STYLES[this.kind()].classes}`);
}
