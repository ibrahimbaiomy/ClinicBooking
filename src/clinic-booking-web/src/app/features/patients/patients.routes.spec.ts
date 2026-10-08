import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideAuthTesting, signIn } from '../../../testing/auth-testing';
import { routes } from '../../app.routes';

describe('Patients routes (D63)', () => {
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting(routes) });
    router = TestBed.inject(Router);
  });

  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  it('a signed-out visitor is sent to login and returns afterwards', async () => {
    await router.navigateByUrl('/patients?q=010');

    expect(router.url).toBe('/login?returnUrl=%2Fpatients%3Fq%3D010');
  });

  it('the list needs patients.read: a signed-in user without it sees /forbidden', async () => {
    await signIn(['patients.create', 'patients.edit', 'patients.delete', 'users.manage']);

    await router.navigateByUrl('/patients');

    expect(router.url).toBe('/forbidden');
  });

  it('with patients.read the list opens', async () => {
    await signIn(['patients.read']);

    await router.navigateByUrl('/patients');

    expect(router.url).toBe('/patients');
  });

  it('a user who must change the password goes there first', async () => {
    await signIn(['patients.read'], 'token-1', { mustChangePassword: true });

    await router.navigateByUrl('/patients');

    expect(router.url).toBe('/change-password?returnUrl=%2Fpatients');
  });
});
