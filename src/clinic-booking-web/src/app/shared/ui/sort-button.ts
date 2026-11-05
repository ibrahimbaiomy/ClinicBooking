import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { SortDirection } from '../list/list-params';

/**
 * The button inside a sortable `th` (D64). It has no strings of its own: the column's label is projected.
 * The icon shows the direction (an up or a down arrow, or a faint pair when the column is not the sort);
 * it is vertical, so it needs no mirroring in RTL, and it is hidden from assistive technology because the
 * `th` carries `aria-sort`.
 */
@Component({
  selector: 'cb-sort-button',
  templateUrl: './sort-button.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SortButton {
  readonly direction = input<SortDirection | null>(null);
  readonly sort = output<void>();
}
