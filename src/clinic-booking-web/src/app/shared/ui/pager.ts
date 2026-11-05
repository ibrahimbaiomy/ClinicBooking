import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { IntlPipe } from '../../core/format/intl.pipe';
import { PAGE_SIZES } from '../list/list-params';

/**
 * One row (D64): previous / "page X of Y" / next on the inline-start side, the page size on the
 * inline-end side; it wraps only when the width cannot hold both. Text only, so there is no icon to
 * mirror in RTL.
 */
@Component({
  selector: 'cb-pager',
  imports: [TranslocoPipe, IntlPipe],
  templateUrl: './pager.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Pager {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly pageSize = input.required<number>();
  /** The id of the page-size select, unique on the page (each list keeps its own, e.g. `clinics-size`). */
  readonly sizeId = input.required<string>();

  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  protected readonly pageSizes = PAGE_SIZES;

  protected onSize(event: Event): void {
    this.pageSizeChange.emit(Number((event.target as HTMLSelectElement).value));
  }
}
