import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

/** Shown to a signed-in user who opens a page they have no permission for (UX only). */
@Component({
  selector: 'cb-forbidden',
  imports: [RouterLink, TranslocoPipe],
  templateUrl: './forbidden.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Forbidden {}
