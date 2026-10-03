import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/** The placeholder content area of the shell. Real screens replace it. */
@Component({
  selector: 'cb-home',
  imports: [TranslocoPipe],
  templateUrl: './home.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Home {}
