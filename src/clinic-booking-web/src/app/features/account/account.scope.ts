import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

export const ACCOUNT_SCOPE = 'account';

/** Loads the scope file for the active language before the page renders, so no raw keys flash. */
export const accountScopeResolver: ResolveFn<boolean> = async () => {
  const transloco = inject(TranslocoService);
  await firstValueFrom(transloco.load(`${ACCOUNT_SCOPE}/${transloco.getActiveLang()}`));
  return true;
};
