import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { flushProblem, provideAuthTesting, signIn, tokenResponse } from '../../../testing/auth-testing';
import { parseApiError } from './api-error';
import { GRACE_RETRY_DELAY_MS, SessionService } from './session.service';

const REFRESH = '/api/auth/refresh';
const UNAUTHORIZED = 'error.auth.unauthorized';

const bearer = (request: TestRequest) => request.request.headers.get('Authorization');

describe('authInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let session: SessionService;

  beforeEach(async () => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting() });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionService);
    vi.spyOn(Math, 'random').mockReturnValue(0);
    await TestBed.inject(Router).navigateByUrl('/specialties');
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    http.verify();
  });

  describe('which requests get the token', () => {
    beforeEach(() => signIn());

    it('adds it to a same-origin /api request', () => {
      client.get('/api/specialties').subscribe();

      expect(bearer(http.expectOne('/api/specialties'))).toBe('Bearer token-1');
    });

    it('adds it to /api/auth/me', () => {
      client.get('/api/auth/me').subscribe();

      expect(bearer(http.expectOne('/api/auth/me'))).toBe('Bearer token-1');
    });

    it('adds it to an absolute URL of the same origin', () => {
      client.get(`${location.origin}/api/specialties`).subscribe();

      expect(bearer(http.expectOne(`${location.origin}/api/specialties`))).toBe('Bearer token-1');
    });

    it.each(['/i18n/ar.json', '/i18n/en.json', '/assets/logo.svg', '/apix/other', '/health/ready'])(
      'does not add it to %s, and a 401 there never triggers a refresh',
      (url) => {
        let failed: unknown;
        client.get(url).subscribe({ error: (error: unknown) => (failed = error) });

        const request = http.expectOne(url);
        expect(bearer(request)).toBeNull();
        flushProblem(request, 401, UNAUTHORIZED);

        expect(failed).toBeInstanceOf(HttpErrorResponse);
        http.expectNone(REFRESH);
        expect(session.state()).toBe('authenticated');
      },
    );

    it('does not add it to another origin, even for an /api path', () => {
      client.get('https://evil.example/api/specialties').subscribe();

      expect(bearer(http.expectOne('https://evil.example/api/specialties'))).toBeNull();
    });

    it.each(['/api/auth/login', '/api/auth/refresh', '/api/auth/logout'])('does not add it to %s', (url) => {
      client.post(url, null).subscribe({ error: () => undefined });

      const request = http.expectOne(url);
      expect(bearer(request)).toBeNull();
      flushProblem(request, 401, UNAUTHORIZED);
      http.expectNone(REFRESH);
    });
  });

  describe('refresh on 401', () => {
    beforeEach(() => signIn());

    it('refreshes once and retries the request once with the new token', () => {
      let body: unknown;
      client.get('/api/specialties').subscribe((value) => (body = value));

      flushProblem(http.expectOne('/api/specialties'), 401, UNAUTHORIZED);
      http.expectOne(REFRESH).flush(tokenResponse('token-2'));
      const retry = http.expectOne('/api/specialties');
      expect(bearer(retry)).toBe('Bearer token-2');
      retry.flush({ items: [] });

      expect(body).toEqual({ items: [] });
      expect(session.accessToken()).toBe('token-2');
      expect(session.state()).toBe('authenticated');
    });

    it('shares ONE refresh between concurrent 401s and retries each original request once', () => {
      const results: string[] = [];
      for (const path of ['a', 'b', 'c']) {
        client.get<string>(`/api/${path}`).subscribe((value) => results.push(value));
      }

      for (const path of ['a', 'b', 'c']) {
        flushProblem(http.expectOne(`/api/${path}`), 401, UNAUTHORIZED);
      }

      http.expectOne(REFRESH).flush(tokenResponse('token-2')); // expectOne fails if there is more than one

      for (const path of ['a', 'b', 'c']) {
        const retry = http.expectOne(`/api/${path}`);
        expect(bearer(retry)).toBe('Bearer token-2');
        retry.flush(path);
      }

      expect(results.sort()).toEqual(['a', 'b', 'c']);
      http.expectNone(REFRESH);
    });

    it('a request that still carried the old token after a refresh retries without a second refresh', () => {
      client.get('/api/slow').subscribe();
      client.get('/api/fast').subscribe();
      const slow = http.expectOne('/api/slow');
      const fast = http.expectOne('/api/fast');

      flushProblem(fast, 401, UNAUTHORIZED);
      http.expectOne(REFRESH).flush(tokenResponse('token-2'));
      http.expectOne('/api/fast').flush({});

      flushProblem(slow, 401, UNAUTHORIZED); // answered late, sent with token-1
      http.expectNone(REFRESH);
      const retry = http.expectOne('/api/slow');
      expect(bearer(retry)).toBe('Bearer token-2');
      retry.flush({});
    });

    it('ends the session when the retried request is rejected again', async () => {
      let failed: unknown;
      client.get('/api/specialties').subscribe({ error: (error: unknown) => (failed = error) });

      flushProblem(http.expectOne('/api/specialties'), 401, UNAUTHORIZED);
      http.expectOne(REFRESH).flush(tokenResponse('token-2'));
      flushProblem(http.expectOne('/api/specialties'), 401, UNAUTHORIZED);

      expect(failed).toBeInstanceOf(HttpErrorResponse);
      http.expectNone(REFRESH);
      expect(session.state()).toBe('anonymous');
      await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fspecialties'));
    });

    it.each([
      ['a 403', 403, 'error.auth.forbidden'],
      ['a 401 with another key', 401, 'error.http.404'],
      ['a 404', 404, 'error.specialty.not_found'],
    ])('does not refresh for %s', (_name, status, key) => {
      let failed: HttpErrorResponse | undefined;
      client.get('/api/specialties').subscribe({ error: (error: HttpErrorResponse) => (failed = error) });

      flushProblem(http.expectOne('/api/specialties'), status, key);

      expect(failed?.status).toBe(status);
      http.expectNone(REFRESH);
      expect(session.state()).toBe('authenticated');
    });
  });

  describe('a refresh that fails during an active session', () => {
    beforeEach(() => signIn());

    function startRequest(): { failed: () => HttpErrorResponse | undefined; done: () => unknown } {
      let failed: HttpErrorResponse | undefined;
      let done: unknown;
      client.get('/api/specialties').subscribe({
        next: (value) => (done = value),
        error: (error: HttpErrorResponse) => (failed = error),
      });
      flushProblem(http.expectOne('/api/specialties'), 401, UNAUTHORIZED);
      return { failed: () => failed, done: () => done };
    }

    it('grace window: retries the refresh once after the delay, then carries on if it works', async () => {
      vi.useFakeTimers();
      const request = startRequest();

      flushProblem(http.expectOne(REFRESH), 401, 'error.auth.invalid_refresh_token');
      await vi.advanceTimersByTimeAsync(GRACE_RETRY_DELAY_MS - 1);
      http.expectNone(REFRESH); // not before the delay
      await vi.advanceTimersByTimeAsync(1);

      http.expectOne(REFRESH).flush(tokenResponse('token-2'));
      const retry = http.expectOne('/api/specialties');
      expect(bearer(retry)).toBe('Bearer token-2');
      retry.flush({ ok: true });

      expect(request.done()).toEqual({ ok: true });
      expect(session.state()).toBe('authenticated');
      expect(TestBed.inject(Router).url).toBe('/specialties');
      http.expectNone(REFRESH);
    });

    it('the delay includes jitter of up to 500 ms', async () => {
      vi.useFakeTimers();
      vi.spyOn(Math, 'random').mockReturnValue(0.999);
      startRequest();

      flushProblem(http.expectOne(REFRESH), 401, 'error.auth.invalid_refresh_token');
      await vi.advanceTimersByTimeAsync(GRACE_RETRY_DELAY_MS + 400);
      http.expectNone(REFRESH);
      await vi.advanceTimersByTimeAsync(100);

      http.expectOne(REFRESH).flush(tokenResponse('token-2'));
      http.expectOne('/api/specialties').flush({});
    });

    it('really logged out: two 401s end the session, redirect with returnUrl and never a third refresh', async () => {
      vi.useFakeTimers();
      const request = startRequest();

      flushProblem(http.expectOne(REFRESH), 401, 'error.auth.invalid_refresh_token');
      await vi.advanceTimersByTimeAsync(GRACE_RETRY_DELAY_MS + 500);
      flushProblem(http.expectOne(REFRESH), 401, 'error.auth.invalid_refresh_token');
      await vi.advanceTimersByTimeAsync(10_000);

      expect(request.failed()?.status).toBe(401);
      expect(session.state()).toBe('anonymous');
      expect(session.accessToken()).toBeNull();
      expect(session.expired()).toBe(true);
      expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fspecialties');
      http.expectNone(REFRESH);
    });

    it('concurrent 401s still share one flight, including the delayed retry', async () => {
      vi.useFakeTimers();
      const requests = ['a', 'b'].map((path) => {
        client.get(`/api/${path}`).subscribe();
        return http.expectOne(`/api/${path}`);
      });
      requests.forEach((request) => flushProblem(request, 401, UNAUTHORIZED));

      flushProblem(http.expectOne(REFRESH), 401, 'error.auth.invalid_refresh_token');
      await vi.advanceTimersByTimeAsync(GRACE_RETRY_DELAY_MS + 500);
      http.expectOne(REFRESH).flush(tokenResponse('token-2'));

      http.expectOne('/api/a').flush({});
      http.expectOne('/api/b').flush({});
      http.expectNone(REFRESH);
    });

    it.each([[429, 'error.auth.rate_limited'], [503, 'error.unexpected']])(
      'a transient %i keeps the session, is not retried, and the request fails with a typed error',
      async (status, key) => {
        vi.useFakeTimers();
        const request = startRequest();

        flushProblem(http.expectOne(REFRESH), status, key);
        await vi.advanceTimersByTimeAsync(10_000);

        expect(parseApiError(request.failed())).toMatchObject({ status, key });
        expect(session.state()).toBe('authenticated');
        expect(session.accessToken()).toBe('token-1');
        http.expectNone(REFRESH);
      },
    );

    it('a network failure on refresh keeps the session', async () => {
      const request = startRequest();

      http.expectOne(REFRESH).error(new ProgressEvent('error'));

      expect(parseApiError(request.failed()).kind).toBe('network');
      expect(session.state()).toBe('authenticated');
    });
  });

  it('a 401 without a token (signed out) does not trigger a refresh', () => {
    let failed: unknown;
    client.get('/api/specialties').subscribe({ error: (error: unknown) => (failed = error) });

    flushProblem(http.expectOne('/api/specialties'), 401, UNAUTHORIZED);

    expect(failed).toBeInstanceOf(HttpErrorResponse);
    http.expectNone(REFRESH);
  });
});
