import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideAuthTesting, signIn } from '../../../testing/auth-testing';
import { routes } from '../../app.routes';

describe('Users and account routes (D59)', () => {
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting(routes) });
    router = TestBed.inject(Router);
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  describe('/users', () => {
    it.each(['/users', '/users/new', '/users/5'])('a signed-out visitor to %s goes to login and returns afterwards', async (url) => {
      await router.navigateByUrl(url);

      expect(router.url).toBe(`/login?returnUrl=${encodeURIComponent(url)}`);
    });

    it.each(['/users', '/users/new', '/users/5'])(
      'without users.manage, %s redirects to /forbidden (UX only: the API enforces it)',
      async (url) => {
        await signIn(['clinics.manage', 'specialties.manage']);

        await router.navigateByUrl(url);

        expect(router.url).toBe('/forbidden');
      },
    );

    it.each(['/users', '/users/new', '/users/5'])('with users.manage, %s opens', async (url) => {
      await signIn(['users.manage']);

      await router.navigateByUrl(url);

      expect(router.url).toBe(url);
    });

    it('a clinic-scoped permission does not open the users pages', async () => {
      await signIn([], 'token-1', {
        clinicPermissions: [{ clinicId: 1, clinicNameAr: 'أ', clinicNameEn: 'A', permissions: ['users.manage'] }],
      });

      await router.navigateByUrl('/users');

      expect(router.url).toBe('/forbidden');
    });

    it.each(['/users', '/users/new', '/users/5'])(
      'a user who must change the password is sent to /change-password from %s, even with users.manage',
      async (url) => {
        await signIn(['users.manage'], 'token-1', { mustChangePassword: true });

        await router.navigateByUrl(url);

        expect(router.url).toBe(`/change-password?returnUrl=${encodeURIComponent(url)}`);
      },
    );
  });

  describe('/change-password', () => {
    it('a signed-out visitor goes to login and returns afterwards', async () => {
      await router.navigateByUrl('/change-password');

      expect(router.url).toBe('/login?returnUrl=%2Fchange-password');
    });

    it('opens for any signed-in user, no permission needed (the voluntary change)', async () => {
      await signIn([]);

      await router.navigateByUrl('/change-password');

      expect(router.url).toBe('/change-password');
    });

    it('opens for a user who must change the password, whatever they asked for first', async () => {
      await signIn([], 'token-1', { mustChangePassword: true });

      await router.navigateByUrl('/change-password?returnUrl=%2Fclinics');

      expect(router.url).toBe('/change-password?returnUrl=%2Fclinics');
    });

    it.each(['/', '/clinics', '/specialties', '/forbidden', '/nowhere'])(
      'a forced user asking for %s lands on /change-password and cannot go anywhere else',
      async (url) => {
        await signIn([], 'token-1', { mustChangePassword: true });

        await router.navigateByUrl(url);

        expect(router.url.startsWith('/change-password')).toBe(true);
      },
    );

    it('a forced user who opens /login ends on /change-password', async () => {
      await signIn([], 'token-1', { mustChangePassword: true });

      await router.navigateByUrl('/login');

      expect(router.url).toBe('/change-password');
    });
  });
});
