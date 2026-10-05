import { Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';
import { ChangePassword } from './change-password/change-password';
import { ACCOUNT_SCOPE, accountScopeResolver } from './account.scope';

/**
 * Lazy routes of the account area, mounted at `/change-password` with `changePasswordGuard` (not
 * `authGuard`: a user who must change the password has to be able to reach this page, D59).
 */
export const ACCOUNT_ROUTES: Routes = [
  {
    path: '',
    providers: [provideTranslocoScope(ACCOUNT_SCOPE)],
    resolve: { scope: accountScopeResolver },
    children: [{ path: '', pathMatch: 'full', component: ChangePassword }],
  },
];
