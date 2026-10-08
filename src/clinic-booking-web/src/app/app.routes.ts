import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { authGuard, changePasswordGuard, guestGuard, permissionGuard } from './core/auth/auth.guard';
import { Permissions } from './core/auth/permissions';
import { Login } from './features/auth/login';
import { Forbidden } from './shell/forbidden';
import { Home } from './shell/home';

/**
 * Deep links such as /specialties are served index.html by the API (D15). A signed-out visitor
 * goes to login and, after signing in, returns to the link (authGuard runs first). Until their
 * screens exist, a signed-in user is sent to the shell.
 */
const unknownRoute: CanActivateFn = () => inject(Router).parseUrl('/');

export const routes: Routes = [
  { path: 'login', canActivate: [guestGuard], component: Login },
  { path: 'forbidden', canActivate: [authGuard], component: Forbidden },
  { path: '', pathMatch: 'full', canActivate: [authGuard], component: Home },
  {
    path: 'clinics',
    canActivate: [authGuard],
    loadChildren: () => import('./features/clinics/clinics.routes').then((m) => m.CLINICS_ROUTES),
  },
  {
    path: 'doctors',
    canActivate: [authGuard],
    loadChildren: () => import('./features/doctors/doctors.routes').then((m) => m.DOCTORS_ROUTES),
  },
  {
    path: 'specialties',
    canActivate: [authGuard],
    loadChildren: () =>
      import('./features/specialties/specialties.routes').then((m) => m.SPECIALTIES_ROUTES),
  },
  {
    path: 'patients',
    canActivate: [authGuard],
    loadChildren: () => import('./features/patients/patients.routes').then((m) => m.PATIENTS_ROUTES),
  },
  {
    // Not behind authGuard: a user who must change the password has to be able to reach it (D59).
    path: 'change-password',
    canActivate: [changePasswordGuard],
    loadChildren: () => import('./features/account/account.routes').then((m) => m.ACCOUNT_ROUTES),
  },
  {
    path: 'users',
    canActivate: [authGuard, permissionGuard(Permissions.UsersManage)],
    loadChildren: () => import('./features/users/users.routes').then((m) => m.USERS_ROUTES),
  },
  { path: '**', canActivate: [authGuard, unknownRoute], component: Home },
];
