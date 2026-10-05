import { Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';
import { UserDetailPage } from './detail/user-detail';
import { UserForm } from './form/user-form';
import { UsersList } from './list/users-list';
import { USERS_SCOPE, usersScopeResolver } from './users.scope';

/**
 * Lazy routes of the feature. The parent route carries `authGuard` and
 * `permissionGuard(Permissions.UsersManage)` (app.routes.ts): every page here needs users.manage (D57).
 * Guards are UX: the API enforces permissions.
 */
export const USERS_ROUTES: Routes = [
  {
    path: '',
    providers: [provideTranslocoScope(USERS_SCOPE)],
    resolve: { scope: usersScopeResolver },
    children: [
      { path: '', pathMatch: 'full', component: UsersList },
      { path: 'new', component: UserForm },
      { path: ':id', component: UserDetailPage },
    ],
  },
];
