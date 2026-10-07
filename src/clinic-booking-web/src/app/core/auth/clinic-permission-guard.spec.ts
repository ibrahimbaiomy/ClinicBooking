import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { provideAuthTesting, signIn, Stub } from '../../../testing/auth-testing';
import { clinicPermissionInAnyGuard } from './auth.guard';
import { ClinicPermissions } from './permissions';

const ROUTES: Routes = [
  { path: 'login', component: Stub },
  { path: 'forbidden', component: Stub },
  { path: 'change-password', component: Stub },
  { path: 'new', canActivate: [clinicPermissionInAnyGuard(ClinicPermissions.DoctorsManage)], component: Stub },
  { path: '**', component: Stub },
];

const grant = (clinicId: number, permissions: string[]) => ({
  clinicId,
  clinicNameAr: 'عيادة',
  clinicNameEn: 'Clinic',
  permissions,
});

describe('clinicPermissionInAnyGuard (D62)', () => {
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting(ROUTES) });
    router = TestBed.inject(Router);
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends a signed-out visitor to login and remembers the target', async () => {
    await router.navigateByUrl('/new');

    expect(router.url).toBe('/login?returnUrl=%2Fnew');
  });

  it('passes when the user holds the permission in any one clinic', async () => {
    await signIn([], 'token-1', { clinicPermissions: [grant(4, ['other.permission']), grant(9, ['doctors.manage'])] });

    await router.navigateByUrl('/new');

    expect(router.url).toBe('/new');
  });

  it('sends a user without it in any clinic to /forbidden, whatever global permissions they hold', async () => {
    await signIn(['users.manage', 'clinics.manage', 'specialties.manage'], 'token-1', {
      clinicPermissions: [grant(4, ['other.permission'])],
    });

    await router.navigateByUrl('/new');

    expect(router.url).toBe('/forbidden');
  });

  it('keeps the forced password change: a user with a temporary password goes to /change-password', async () => {
    await signIn([], 'token-1', { mustChangePassword: true, clinicPermissions: [grant(9, ['doctors.manage'])] });

    await router.navigateByUrl('/new');

    expect(router.url).toBe('/change-password?returnUrl=%2Fnew');
  });
});
