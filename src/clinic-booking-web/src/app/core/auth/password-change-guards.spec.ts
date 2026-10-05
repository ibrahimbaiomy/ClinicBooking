import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { provideAuthTesting, signIn, Stub } from '../../../testing/auth-testing';
import { authGuard, changePasswordGuard, guestGuard, permissionGuard } from './auth.guard';
import { SessionService } from './session.service';

const ROUTES: Routes = [
  { path: 'login', canActivate: [guestGuard], component: Stub },
  { path: 'change-password', canActivate: [changePasswordGuard], component: Stub },
  { path: 'forbidden', canActivate: [authGuard], component: Stub },
  { path: 'secret', canActivate: [authGuard], component: Stub },
  { path: 'admin', canActivate: [authGuard, permissionGuard('users.manage')], component: Stub },
  { path: '', pathMatch: 'full', canActivate: [authGuard], component: Stub },
];

const FORCED = { mustChangePassword: true };

describe('forced password change in the route guards (D59)', () => {
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting(ROUTES) });
    router = TestBed.inject(Router);
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  describe('authGuard', () => {
    it('sends a forced user to /change-password and remembers the target', async () => {
      await signIn([], 'token-1', FORCED);

      await router.navigateByUrl('/secret?tab=2');

      expect(router.url).toBe('/change-password?returnUrl=%2Fsecret%3Ftab%3D2');
    });

    it('omits the returnUrl for the home page', async () => {
      await signIn([], 'token-1', FORCED);

      await router.navigateByUrl('/');

      expect(router.url).toBe('/change-password');
    });

    it('a forced user cannot reach /forbidden either', async () => {
      await signIn([], 'token-1', FORCED);

      await router.navigateByUrl('/forbidden');

      expect(router.url).toBe('/change-password?returnUrl=%2Fforbidden');
    });

    it('does not interfere with a user who is not forced', async () => {
      await signIn([]);

      await router.navigateByUrl('/secret');

      expect(router.url).toBe('/secret');
    });

    it('lets the user in once the flag is cleared', async () => {
      await signIn([], 'token-1', FORCED);
      await router.navigateByUrl('/secret');
      expect(router.url.startsWith('/change-password')).toBe(true);

      TestBed.inject(SessionService).clearPasswordChangeRequired();
      await router.navigateByUrl('/secret');

      expect(router.url).toBe('/secret');
    });
  });

  describe('permissionGuard', () => {
    it('sends a forced user to /change-password, not to /forbidden, even with the permission', async () => {
      await signIn(['users.manage'], 'token-1', FORCED);

      await router.navigateByUrl('/admin');

      expect(router.url).toBe('/change-password?returnUrl=%2Fadmin');
    });

    it('still answers /forbidden for a user who is not forced and lacks the permission', async () => {
      await signIn([]);

      await router.navigateByUrl('/admin');

      expect(router.url).toBe('/forbidden');
    });
  });

  describe('changePasswordGuard', () => {
    it('sends a signed-out user to login, remembering the page', async () => {
      await router.navigateByUrl('/change-password');

      expect(router.url).toBe('/login?returnUrl=%2Fchange-password');
    });

    it('lets a forced user in and never redirects them (no loop)', async () => {
      await signIn([], 'token-1', FORCED);

      await router.navigateByUrl('/change-password?returnUrl=%2Fsecret');

      expect(router.url).toBe('/change-password?returnUrl=%2Fsecret');
    });

    it('lets a user who is not forced in (the voluntary change)', async () => {
      await signIn([]);

      await router.navigateByUrl('/change-password');

      expect(router.url).toBe('/change-password');
    });
  });

  describe('guestGuard', () => {
    it('a forced user who opens /login ends on /change-password, with no loop', async () => {
      await signIn([], 'token-1', FORCED);

      await router.navigateByUrl('/login');

      expect(router.url).toBe('/change-password');
    });

    it('keeps the returnUrl through login and the forced redirect', async () => {
      await signIn([], 'token-1', FORCED);

      await router.navigateByUrl('/login?returnUrl=%2Fsecret');

      expect(router.url).toBe('/change-password?returnUrl=%2Fsecret');
    });

    it('a forced user who opens /login?returnUrl=/change-password stays there (one hop)', async () => {
      await signIn([], 'token-1', FORCED);

      await router.navigateByUrl('/login?returnUrl=%2Fchange-password');

      expect(router.url).toBe('/change-password');
    });
  });
});
