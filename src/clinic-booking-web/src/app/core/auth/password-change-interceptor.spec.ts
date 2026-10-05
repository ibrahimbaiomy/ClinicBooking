import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { flushProblem, provideAuthTesting, signIn } from '../../../testing/auth-testing';
import { SessionService } from './session.service';

const KEY = 'error.auth.password_change_required';

describe('authInterceptor: 403 error.auth.password_change_required (D59)', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let session: SessionService;
  let router: Router;

  beforeEach(async () => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting() });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionService);
    router = TestBed.inject(Router);
    await signIn([]);
    await router.navigateByUrl('/specialties?page=3');
  });

  afterEach(() => http.verify());

  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  it('sends the user to /change-password, remembering the page, and still fails the request', async () => {
    let failure: unknown;
    client.get('/api/specialties').subscribe({ error: (error: unknown) => (failure = error) });

    flushProblem(http.expectOne('/api/specialties'), 403, KEY);
    await settle();

    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect(session.mustChangePassword()).toBe(true);
    expect(router.url).toBe('/change-password?returnUrl=%2Fspecialties%3Fpage%3D3');
  });

  it('never refreshes or retries: it is not a 401', async () => {
    client.get('/api/clinics').subscribe({ error: () => undefined });

    flushProblem(http.expectOne('/api/clinics'), 403, KEY);
    await settle();

    http.expectNone('/api/auth/refresh');
    http.expectNone('/api/clinics');
    expect(session.state()).toBe('authenticated');
  });

  it('several requests failing together navigate once', async () => {
    const navigate = vi.spyOn(router, 'navigate');
    client.get('/api/specialties').subscribe({ error: () => undefined });
    client.get('/api/clinics').subscribe({ error: () => undefined });
    client.get('/api/users').subscribe({ error: () => undefined });

    for (const url of ['/api/specialties', '/api/clinics', '/api/users']) {
      flushProblem(http.expectOne(url), 403, KEY);
    }
    await settle();

    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('on the change-password page it does not navigate again (no loop)', async () => {
    await router.navigateByUrl('/change-password?returnUrl=%2Fclinics');
    const navigate = vi.spyOn(router, 'navigate');
    client.get('/api/clinics').subscribe({ error: () => undefined });

    flushProblem(http.expectOne('/api/clinics'), 403, KEY);
    await settle();

    expect(navigate).not.toHaveBeenCalled();
    expect(router.url).toBe('/change-password?returnUrl=%2Fclinics');
  });

  it('another 403 key is passed through untouched', async () => {
    client.get('/api/clinics').subscribe({ error: () => undefined });

    flushProblem(http.expectOne('/api/clinics'), 403, 'error.auth.forbidden');
    await settle();

    expect(session.mustChangePassword()).toBe(false);
    expect(router.url).toBe('/specialties?page=3');
  });

  it.each(['/api/auth/login', '/api/auth/refresh', '/api/auth/logout'])(
    '%s is exempt: no redirect from the authentication endpoints',
    async (url) => {
      client.post(url, null).subscribe({ error: () => undefined });

      flushProblem(http.expectOne(url), 403, KEY);
      await settle();

      expect(session.mustChangePassword()).toBe(false);
      expect(router.url).toBe('/specialties?page=3');
    },
  );

  it('a request to another origin or a translation file is ignored', async () => {
    client.get('/i18n/ar.json').subscribe({ error: () => undefined });

    flushProblem(http.expectOne('/i18n/ar.json'), 403, KEY);
    await settle();

    expect(session.mustChangePassword()).toBe(false);
  });
});
