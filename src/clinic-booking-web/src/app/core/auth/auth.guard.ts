import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { safeReturnUrl } from './return-url';
import { CHANGE_PASSWORD_URL, SessionService } from './session.service';

function loginTree(router: Router, url: string) {
  const returnUrl = safeReturnUrl(url);
  return router.createUrlTree(['/login'], { queryParams: returnUrl === '/' ? {} : { returnUrl } });
}

/** A user with a temporary password can reach only the change-password page (D58, D59). */
function changePasswordTree(router: Router, url: string) {
  const returnUrl = safeReturnUrl(url);
  return router.createUrlTree([CHANGE_PASSWORD_URL], { queryParams: returnUrl === '/' ? {} : { returnUrl } });
}

/**
 * Signed-out users go to /login and come back to where they were headed. A signed-in user who must
 * change the password goes to /change-password and comes back after it (D59). The change-password
 * route itself uses `changePasswordGuard`, so there is no redirect loop.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);
  await session.whenReady();
  if (!session.isAuthenticated()) {
    return loginTree(router, state.url);
  }
  return session.mustChangePassword() ? changePasswordTree(router, state.url) : true;
};

/** /change-password: any signed-in user, forced or not. It never redirects a forced user (no loop). */
export const changePasswordGuard: CanActivateFn = async (_route, state) => {
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
    if (session.mustChangePassword()) {
      return changePasswordTree(router, state.url);
    }
    return session.can(permission) || router.parseUrl('/forbidden');
  };
}

/**
 * UX only (D62): passes when the user holds a clinic-scoped permission in at least one live clinic
 * (`canInAny`), otherwise /forbidden. Keeps authGuard's redirects, the forced password change included.
 */
export function clinicPermissionInAnyGuard(permission: string): CanActivateFn {
  return async (_route, state) => {
    const session = inject(SessionService);
    const router = inject(Router);
    await session.whenReady();
    if (!session.isAuthenticated()) {
      return loginTree(router, state.url);
    }
    if (session.mustChangePassword()) {
      return changePasswordTree(router, state.url);
    }
    return session.canInAny(permission) || router.parseUrl('/forbidden');
  };
}
