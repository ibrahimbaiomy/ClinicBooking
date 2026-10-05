import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { flushProblem, meBody, provideAuthTesting, signIn } from '../../../testing/auth-testing';
import { clinicGrant } from '../../../testing/users-testing';
import { ClinicPermissions, Permissions } from './permissions';
import { SessionService } from './session.service';

describe('session model: temporary password and clinic-scoped permissions (D59)', () => {
  let session: SessionService;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting() });
    session = TestBed.inject(SessionService);
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  describe('mustChangePassword', () => {
    it('is false for an ordinary user and for nobody signed in', async () => {
      expect(session.mustChangePassword()).toBe(false);

      await signIn([]);

      expect(session.mustChangePassword()).toBe(false);
    });

    it('is true when /me says so', async () => {
      await signIn([], 'token-1', { mustChangePassword: true });

      expect(session.mustChangePassword()).toBe(true);
    });

    it('tolerates an answer without the field (an old server)', async () => {
      const done = session.login('someone', 'a-test-value');
      done.subscribe();
      http.expectOne('/api/auth/login').flush({ accessToken: 't', expiresAt: '2030-01-01T00:00:00Z' });
      http.expectOne('/api/auth/me').flush({ id: 1, userName: 'someone', permissions: [] });

      expect(session.mustChangePassword()).toBe(false);
      expect(session.canIn('doctors.manage', 1)).toBe(false);
    });
  });

  describe('can, canIn and canInAny', () => {
    const grants = [clinicGrant(5, 'عيادة', 'Clinic', [ClinicPermissions.DoctorsManage])];

    it('can() stays global only: a clinic-scoped permission does not answer it', async () => {
      await signIn([Permissions.ClinicsManage], 'token-1', { clinicPermissions: grants });

      expect(session.can(Permissions.ClinicsManage)).toBe(true);
      expect(session.can(ClinicPermissions.DoctorsManage)).toBe(false);
    });

    it('canIn() answers only for the clinic that holds the permission', async () => {
      await signIn([], 'token-1', { clinicPermissions: grants });

      expect(session.canIn(ClinicPermissions.DoctorsManage, 5)).toBe(true);
      expect(session.canIn(ClinicPermissions.DoctorsManage, 6)).toBe(false);
      expect(session.canIn('doctors.other', 5)).toBe(false);
    });

    it('canIn() takes the clinic id as a number or as a string (int64 typing)', async () => {
      await signIn([], 'token-1', { clinicPermissions: [clinicGrant('5', 'عيادة', 'Clinic', ['doctors.manage'])] });

      expect(session.canIn('doctors.manage', 5)).toBe(true);
      expect(session.canIn('doctors.manage', '5')).toBe(true);
    });

    it('a global permission never satisfies canIn()', async () => {
      await signIn(Object.values(Permissions), 'token-1', { clinicPermissions: [] });

      expect(session.canIn(ClinicPermissions.DoctorsManage, 5)).toBe(false);
      expect(session.canInAny(ClinicPermissions.DoctorsManage)).toBe(false);
    });

    it('canInAny() is true when any clinic holds the permission', async () => {
      await signIn([], 'token-1', {
        clinicPermissions: [clinicGrant(1, 'أ', 'A', []), ...grants],
      });

      expect(session.canInAny(ClinicPermissions.DoctorsManage)).toBe(true);
      expect(session.canInAny('doctors.other')).toBe(false);
    });

    it('nothing is held when signed out', () => {
      expect(session.canIn(ClinicPermissions.DoctorsManage, 5)).toBe(false);
      expect(session.canInAny(ClinicPermissions.DoctorsManage)).toBe(false);
    });
  });

  describe('isCurrentUser (id as number or string)', () => {
    it.each([
      [7, 7, true],
      [7, '7', true],
      ['7', 7, true],
      ['7', '7', true],
      [7, 8, false],
      ['7', '8', false],
    ])('the signed-in id %j against %j is %s', async (own, other, expected) => {
      await signIn([], 'token-1', { id: own });

      expect(session.isCurrentUser(other)).toBe(expected);
    });

    it('is false for nobody signed in', () => {
      expect(session.isCurrentUser(1)).toBe(false);
    });
  });

  describe('refreshUser', () => {
    it('reads /me again so the session matches the server', async () => {
      await signIn([Permissions.UsersManage]);

      const refreshing = session.refreshUser();
      http.expectOne('/api/auth/me').flush(meBody({ permissions: [Permissions.ClinicsManage] }));

      expect(await refreshing).toBe(true);
      expect(session.can(Permissions.UsersManage)).toBe(false);
      expect(session.can(Permissions.ClinicsManage)).toBe(true);
    });

    it('returns false and changes nothing when the call fails', async () => {
      await signIn([Permissions.UsersManage]);

      const refreshing = session.refreshUser();
      flushProblem(http.expectOne('/api/auth/me'), 500, 'error.unexpected');

      expect(await refreshing).toBe(false);
      expect(session.can(Permissions.UsersManage)).toBe(true);
    });
  });

  describe('requirePasswordChange', () => {
    beforeEach(async () => {
      await signIn([]);
      await router.navigateByUrl('/clinics?page=2');
    });

    it('marks the user and goes to /change-password with the current url as returnUrl', async () => {
      session.requirePasswordChange();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(session.mustChangePassword()).toBe(true);
      expect(router.url).toBe('/change-password?returnUrl=%2Fclinics%3Fpage%3D2');
    });

    it('does not navigate when already on the page (no loop), whatever the query', async () => {
      await router.navigateByUrl('/change-password?returnUrl=%2Fclinics');
      const navigate = vi.spyOn(router, 'navigate');

      session.requirePasswordChange();
      session.requirePasswordChange();

      expect(session.mustChangePassword()).toBe(true);
      expect(navigate).not.toHaveBeenCalled();
    });

    it('concurrent calls navigate once', async () => {
      const navigate = vi.spyOn(router, 'navigate');

      session.requirePasswordChange();
      session.requirePasswordChange();
      session.requirePasswordChange();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(navigate).toHaveBeenCalledTimes(1);
    });

    it('omits the returnUrl when the user was at the home page', async () => {
      await router.navigateByUrl('/');

      session.requirePasswordChange();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(router.url).toBe('/change-password');
    });

    it('clearPasswordChangeRequired() undoes the local flag', () => {
      session.requirePasswordChange();

      session.clearPasswordChangeRequired();

      expect(session.mustChangePassword()).toBe(false);
    });
  });
});
