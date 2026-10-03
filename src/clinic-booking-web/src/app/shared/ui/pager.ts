import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/** Previous / next with "page X of Y". Text only, so there is no icon to mirror in RTL. */
@Component({
  selector: 'cb-pager',
  imports: [TranslocoPipe],
  templateUrl: './pager.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Pager {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly pageChange = output<number>();
}
