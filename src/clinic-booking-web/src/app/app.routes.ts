import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guard';
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
    path: 'specialties',
    canActivate: [authGuard],
    loadChildren: () =>
      import('./features/specialties/specialties.routes').then((m) => m.SPECIALTIES_ROUTES),
  },
  { path: '**', canActivate: [authGuard, unknownRoute], component: Home },
];
