import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { safeReturnUrl } from './return-url';
import { SessionService } from './session.service';

function loginTree(router: Router, url: string) {
  const returnUrl = safeReturnUrl(url);
  return router.createUrlTree(['/login'], { queryParams: returnUrl === '/' ? {} : { returnUrl } });
}

/** Signed-out users go to /login and come back to where they were headed. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);
  await session.whenReady();
  return session.isAuthenticated() || loginTree(router, state.url);
};

/** The login page is for guests: a signed-in user is sent on to the (validated) returnUrl. */
export const guestGuard: CanActivateFn = async (route) => {
  const session = inject(SessionService);
  const router = inject(Router);
  await session.whenReady();
  return session.isAuthenticated()
    ? router.parseUrl(safeReturnUrl(route.queryParamMap.get('returnUrl')))
    : true;
};

/** UX only: signed-in users without the permission see /forbidden. The API enforces it anyway. */
export function permissionGuard(permission: string): CanActivateFn {
  return async (_route, state) => {
    const session = inject(SessionService);
    const router = inject(Router);
    await session.whenReady();
    if (!session.isAuthenticated()) {
      return loginTree(router, state.url);
    }
    return session.can(permission) || router.parseUrl('/forbidden');
  };
}
