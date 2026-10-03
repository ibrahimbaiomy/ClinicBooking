import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { flushProblem, provideAuthTesting, signIn, Stub, tokenResponse } from '../../../testing/auth-testing';
import { authGuard, guestGuard, permissionGuard } from './auth.guard';
import { SessionService } from './session.service';

const ROUTES: Routes = [
  { path: 'login', canActivate: [guestGuard], component: Stub },
  { path: 'forbidden', component: Stub },
  { path: 'secret', canActivate: [authGuard], component: Stub },
  { path: 'admin', canActivate: [permissionGuard('users.manage')], component: Stub },
  { path: '**', component: Stub },
];

describe('route guards', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: provideAuthTesting(ROUTES) });
    router = TestBed.inject(Router);
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  describe('authGuard', () => {
    it('sends a signed-out user to login and remembers the target', async () => {
      await router.navigateByUrl('/secret?tab=2');

      expect(router.url).toBe('/login?returnUrl=%2Fsecret%3Ftab%3D2');
    });

    it('lets a signed-in user through', async () => {
      await signIn();

      await router.navigateByUrl('/secret');

      expect(router.url).toBe('/secret');
    });

    it('waits for the startup restore instead of redirecting too early', async () => {
      const http = TestBed.inject(HttpTestingController);
      const restore = TestBed.inject(SessionService).restore();

      const navigation = router.navigateByUrl('/secret');
      http.expectOne('/api/auth/refresh').flush(tokenResponse('t'));
      http.expectOne('/api/auth/me').flush({ id: 1, userName: 'u', permissions: [] });
      await restore;
      await navigation;

      expect(router.url).toBe('/secret');
    });

    it('sends the user to login when the restore fails', async () => {
      const http = TestBed.inject(HttpTestingController);
      const restore = TestBed.inject(SessionService).restore();

      const navigation = router.navigateByUrl('/secret');
      flushProblem(http.expectOne('/api/auth/refresh'), 401, 'error.auth.invalid_refresh_token');
      await restore;
      await navigation;

      expect(router.url).toBe('/login?returnUrl=%2Fsecret');
    });
  });

  describe('guestGuard', () => {
    it('shows the login page to a signed-out user', async () => {
      await router.navigateByUrl('/login');

      expect(router.url).toBe('/login');
    });

    it('sends a signed-in user to the returnUrl', async () => {
      await signIn();

      await router.navigateByUrl('/login?returnUrl=%2Fsecret');

      expect(router.url).toBe('/secret');
    });

    it.each(['https://evil.com', '//evil.com', '/\\evil.com', 'javascript:alert(1)', '/login'])(
      'ignores an unsafe returnUrl (%s) and goes to /',
      async (returnUrl) => {
        await signIn();

        await router.navigateByUrl(`/login?returnUrl=${encodeURIComponent(returnUrl)}`);

        expect(router.url).toBe('/');
      },
    );
  });

  describe('permissionGuard', () => {
    it('lets a user with the permission in', async () => {
      await signIn(['users.manage']);

      await router.navigateByUrl('/admin');

      expect(router.url).toBe('/admin');
    });

    it('sends a signed-in user without the permission to /forbidden', async () => {
      await signIn(['specialties.manage']);

      await router.navigateByUrl('/admin');

      expect(router.url).toBe('/forbidden');
    });

    it('sends a signed-out user to login, not to /forbidden', async () => {
      await router.navigateByUrl('/admin');

      expect(router.url).toBe('/login?returnUrl=%2Fadmin');
    });
  });
});
