import { HttpClient } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { flushProblem, provideAuthTesting, signIn, tokenResponse } from '../../../testing/auth-testing';
import { parseApiError } from './api-error';
import { RESTORE_TIMEOUT_MS, SessionService } from './session.service';

const REFRESH = '/api/auth/refresh';
const ME = '/api/auth/me';

describe('SessionService', () => {
  let session: SessionService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting() });
    session = TestBed.inject(SessionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  describe('login', () => {
    it('signs in, loads the user and keeps the token in memory only', async () => {
      await signIn(['specialties.manage'], 'secret-token-value');

      expect(session.state()).toBe('authenticated');
      expect(session.user()?.userName).toBe('someone');
      expect(session.can('specialties.manage')).toBe(true);
      expect(session.can('users.manage')).toBe(false);
      expect(session.accessToken()).toBe('secret-token-value');

      const stored = JSON.stringify([{ ...localStorage }, { ...sessionStorage }]);
      expect(stored).not.toContain('secret-token-value');
      expect(localStorage.length).toBe(0);
      expect(sessionStorage.length).toBe(0);
    });

    it('sends the user name and password as the body and no Authorization header', () => {
      session.login('someone', 'a-test-value').subscribe();

      const request = http.expectOne('/api/auth/login');
      expect(request.request.body).toEqual({ userName: 'someone', password: 'a-test-value' });
      expect(request.request.headers.has('Authorization')).toBe(false);
      request.flush(tokenResponse('t'));
      http.expectOne(ME).flush({ id: 1, userName: 'someone', permissions: [] });
    });

    it.each([
      [401, 'error.auth.invalid_credentials'],
      [423, 'error.auth.locked_out'],
      [429, 'error.auth.rate_limited'],
      [400, 'error.validation.failed'],
    ])('maps a %i failure to %s and stays signed out with no token', async (status, key) => {
      const result = firstValueFrom(session.login('someone', 'wrong'));
      const failed = result.catch((error: unknown) => parseApiError(error));

      flushProblem(http.expectOne('/api/auth/login'), status, key);

      const error = await failed;
      expect(error).toMatchObject({ status, key });
      expect(session.state()).toBe('anonymous');
      expect(session.accessToken()).toBeNull();
    });

    it('does not stay signed in when /me fails after a successful login', async () => {
      const result = firstValueFrom(session.login('someone', 'x')).catch(() => 'failed');

      http.expectOne('/api/auth/login').flush(tokenResponse('t'));
      flushProblem(http.expectOne(ME), 500, 'error.unexpected');

      expect(await result).toBe('failed');
      expect(session.state()).toBe('anonymous');
      expect(session.accessToken()).toBeNull();
    });
  });

  describe('restore at startup', () => {
    it('signs in from the refresh cookie', async () => {
      const done = session.restore();

      http.expectOne(REFRESH).flush(tokenResponse('restored'));
      http.expectOne(ME).flush({ id: 2, userName: 'back', permissions: ['users.manage'] });
      await done;

      expect(session.state()).toBe('authenticated');
      expect(session.user()?.userName).toBe('back');
      expect(session.accessToken()).toBe('restored');
      expect(localStorage.length + sessionStorage.length).toBe(0);
    });

    it('means anonymous at once on a 401, with exactly one refresh call and no retry', async () => {
      vi.useFakeTimers();
      const done = session.restore();

      flushProblem(http.expectOne(REFRESH), 401, 'error.auth.invalid_refresh_token');
      await done;
      await vi.advanceTimersByTimeAsync(5000);

      expect(session.state()).toBe('anonymous');
      http.expectNone(REFRESH);
    });

    it.each([[429], [500]])('means anonymous on a %i', async (status) => {
      const done = session.restore();

      flushProblem(http.expectOne(REFRESH), status, 'error.unexpected');
      await done;

      expect(session.state()).toBe('anonymous');
    });

    it('means anonymous on a network failure', async () => {
      const done = session.restore();

      http.expectOne(REFRESH).error(new ProgressEvent('error'));
      await done;

      expect(session.state()).toBe('anonymous');
    });

    it('gives up after the timeout instead of hanging the first render', async () => {
      vi.useFakeTimers();
      const done = session.restore();
      http.expectOne(REFRESH);

      await vi.advanceTimersByTimeAsync(RESTORE_TIMEOUT_MS + 1);
      await done;

      expect(session.state()).toBe('anonymous');
      expect(session.accessToken()).toBeNull();
    });

    it('runs once however often it is asked', async () => {
      const first = session.restore();
      const second = session.restore();

      flushProblem(http.expectOne(REFRESH), 401, 'error.auth.invalid_refresh_token');
      await Promise.all([first, second]);

      expect(first).toBe(second);
    });
  });

  describe('logout', () => {
    it('calls POST /api/auth/logout without a token and clears the state', async () => {
      await signIn(['users.manage']);
      const done = session.logout();

      const request = http.expectOne('/api/auth/logout');
      expect(request.request.method).toBe('POST');
      expect(request.request.headers.has('Authorization')).toBe(false);
      request.flush(null, { status: 204, statusText: 'No Content' });
      await done;

      expect(session.state()).toBe('anonymous');
      expect(session.user()).toBeNull();
      expect(session.accessToken()).toBeNull();
      expect(session.can('users.manage')).toBe(false);
    });

    it('clears the state even when the server cannot be reached', async () => {
      await signIn();
      const done = session.logout();

      http.expectOne('/api/auth/logout').error(new ProgressEvent('error'));
      await done;

      expect(session.state()).toBe('anonymous');
      expect(session.accessToken()).toBeNull();
    });

    it('a request after logout carries no Authorization header', async () => {
      await signIn();
      const done = session.logout();
      http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
      await done;

      TestBed.inject(HttpClient).get('/api/specialties').subscribe();

      expect(http.expectOne('/api/specialties').request.headers.has('Authorization')).toBe(false);
    });
  });
});
