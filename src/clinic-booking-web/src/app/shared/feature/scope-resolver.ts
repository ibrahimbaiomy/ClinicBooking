import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

/** Loads a translation scope for the active language before the page renders, so no raw keys flash (D53, D62). */
export function scopeResolver(scope: string): ResolveFn<boolean> {
  return async () => {
    const transloco = inject(TranslocoService);
    await firstValueFrom(transloco.load(`${scope}/${transloco.getActiveLang()}`));
    return true;
  };
}
