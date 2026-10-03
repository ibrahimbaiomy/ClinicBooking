import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideAuthTesting, signIn } from '../../../testing/auth-testing';
import { routes } from '../../app.routes';

describe('Specialties routes', () => {
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting(routes) });
    router = TestBed.inject(Router);
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('a signed-out visitor is sent to login and returns to the page afterwards', async () => {
    await router.navigateByUrl('/specialties?page=2');

    expect(router.url).toBe('/login?returnUrl=%2Fspecialties%3Fpage%3D2');
  });

  it('any signed-in user may open the list (reading needs no permission, D50)', async () => {
    await signIn([]);

    await router.navigateByUrl('/specialties');

    expect(router.url).toBe('/specialties');
  });

  it.each(['/specialties/new', '/specialties/1/edit'])(
    'without specialties.manage, %s redirects to /forbidden (UX only: the API enforces it)',
    async (url) => {
      await signIn(['users.manage']);

      await router.navigateByUrl(url);

      expect(router.url).toBe('/forbidden');
    },
  );

  it.each(['/specialties/new', '/specialties/1/edit'])('with specialties.manage, %s opens', async (url) => {
    await signIn(['specialties.manage']);

    await router.navigateByUrl(url);

    expect(router.url).toBe(url);
  });

  it('a signed-out visitor never reaches the form pages either', async () => {
    await router.navigateByUrl('/specialties/new');

    expect(router.url).toBe('/login?returnUrl=%2Fspecialties%2Fnew');
  });
});
