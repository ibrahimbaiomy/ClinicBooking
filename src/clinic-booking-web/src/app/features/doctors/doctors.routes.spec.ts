import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideAuthTesting, signIn } from '../../../testing/auth-testing';
import { routes } from '../../app.routes';

describe('Doctors routes (D62)', () => {
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting(routes) });
    router = TestBed.inject(Router);
  });

  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  it('a signed-out visitor is sent to login and returns to the list afterwards', async () => {
    await router.navigateByUrl('/doctors?clinic=2');

    expect(router.url).toBe('/login?returnUrl=%2Fdoctors%3Fclinic%3D2');
  });

  it('any signed-in user may open the list (reading needs no permission, D57)', async () => {
    await signIn([]);

    await router.navigateByUrl('/doctors');

    expect(router.url).toBe('/doctors');
  });

  it('a user who must change the password goes to /change-password first', async () => {
    await signIn([], 'token-1', { mustChangePassword: true });

    await router.navigateByUrl('/doctors');

    expect(router.url).toBe('/change-password?returnUrl=%2Fdoctors');
  });
});
