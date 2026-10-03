import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/auth/session.service';
import { provideAuthTesting, flushProblem, Stub, tokenResponse } from '../../../testing/auth-testing';
import { arabicTranslations as ar, englishTranslations as en } from '../../../testing/transloco-testing';
import { Login } from './login';

const ROUTES: Routes = [
  { path: 'login', component: Login },
  { path: '**', component: Stub },
];

describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let http: HttpTestingController;
  let router: Router;

  const root = () => fixture.nativeElement as HTMLElement;
  const input = (id: string) => root().querySelector<HTMLInputElement>(`#${id}`)!;
  const submitButton = () => root().querySelector<HTMLButtonElement>('button[type=submit]')!;
  const alertText = () => root().querySelector('[role=alert]')?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  async function render(returnUrl?: string): Promise<void> {
    TestBed.configureTestingModule({ providers: provideAuthTesting(ROUTES) });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    await TestBed.inject(LanguageService).load();
    await router.navigateByUrl(returnUrl ? `/login?returnUrl=${encodeURIComponent(returnUrl)}` : '/login');
    fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
  }

  async function fill(userName: string, password: string): Promise<void> {
    for (const [id, value] of [['login-user-name', userName], ['login-password', password]] as const) {
      input(id).value = value;
      input(id).dispatchEvent(new Event('input'));
    }
    await fixture.whenStable();
  }

  async function submit(): Promise<void> {
    root().querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => http.verify());

  it('has labelled fields with the right autocomplete hints and a live error region', async () => {
    await render();

    expect(root().querySelector('label[for=login-user-name]')?.textContent?.trim()).toBe(ar.login.user_name);
    expect(root().querySelector('label[for=login-password]')?.textContent?.trim()).toBe(ar.login.password);
    expect(input('login-user-name').getAttribute('autocomplete')).toBe('username');
    expect(input('login-password').getAttribute('autocomplete')).toBe('current-password');
    expect(input('login-password').type).toBe('password');
    expect(root().querySelector('[role=alert]')?.getAttribute('aria-live')).toBe('assertive');
    expect(submitButton().textContent?.trim()).toBe(ar.login.submit);
  });

  it('shows validation messages as translated keys and focuses the first invalid field', async () => {
    await render();
    document.body.appendChild(root());

    await submit();

    expect(root().textContent).toContain(ar.error.auth.user_name_required);
    expect(root().textContent).toContain(ar.error.auth.password_required);
    expect(input('login-user-name').getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(input('login-user-name'));
    http.expectNone('/api/auth/login');
    root().remove();
  });

  it('follows the language: the same messages in English', async () => {
    await render();
    TestBed.inject(LanguageService).set('en');
    await TestBed.inject(LanguageService).load();

    await submit();

    expect(root().textContent).toContain(en.error.auth.user_name_required);
  });

  it('rejects a value longer than the API allows without calling it', async () => {
    await render();
    await fill('a'.repeat(257), 'x');

    await submit();

    expect(root().textContent).toContain(ar.error.auth.field_too_long);
    http.expectNone('/api/auth/login');
  });

  it('signs in and goes to the validated returnUrl', async () => {
    await render('/specialties');
    await fill('someone', 'a-test-value');

    const done = submit();
    http.expectOne('/api/auth/login').flush(tokenResponse('t'));
    http.expectOne('/api/auth/me').flush({ id: 1, userName: 'someone', permissions: [] });
    await done;
    await fixture.whenStable();

    expect(router.url).toBe('/specialties');
    expect(TestBed.inject(SessionService).state()).toBe('authenticated');
  });

  it.each(['https://evil.com', '//evil.com', 'javascript:alert(1)'])(
    'ignores an unsafe returnUrl (%s) and goes to /',
    async (returnUrl) => {
      await render(returnUrl);
      await fill('someone', 'a-test-value');

      const done = submit();
      http.expectOne('/api/auth/login').flush(tokenResponse('t'));
      http.expectOne('/api/auth/me').flush({ id: 1, userName: 'someone', permissions: [] });
      await done;

      expect(router.url).toBe('/');
    },
  );

  it('is disabled and busy while the request is pending', async () => {
    await render();
    await fill('someone', 'a-test-value');

    const done = submit();
    await fixture.whenStable();

    expect(submitButton().disabled).toBe(true);
    expect(submitButton().getAttribute('aria-busy')).toBe('true');
    expect(submitButton().textContent?.trim()).toBe(ar.login.submitting);

    flushProblem(http.expectOne('/api/auth/login'), 401, 'error.auth.invalid_credentials');
    await done;
    await fixture.whenStable();
    expect(submitButton().disabled).toBe(false);
  });

  it('shows the same message for an unknown user and a wrong password, and clears the password', async () => {
    await render();
    const shown: string[] = [];

    for (const userName of ['nobody-here', 'a-real-user']) {
      await fill(userName, 'wrong-value');
      const done = submit();
      flushProblem(http.expectOne('/api/auth/login'), 401, 'error.auth.invalid_credentials');
      await done;
      await fixture.whenStable();
      shown.push(alertText());
      expect(input('login-password').value).toBe('');
    }

    expect(shown[0]).toBe(ar.error.auth.invalid_credentials);
    expect(shown[1]).toBe(shown[0]);
  });

  it('shows the lockout message with a retry hint in minutes from Retry-After', async () => {
    await render();
    await fill('someone', 'x');

    const done = submit();
    flushProblem(http.expectOne('/api/auth/login'), 423, 'error.auth.locked_out', { headers: { 'Retry-After': '900' } });
    await done;
    await fixture.whenStable();

    expect(alertText()).toContain(ar.error.auth.locked_out);
    expect(alertText()).toContain(ar.login.retry_after.replace('{{ minutes }}', '15'));
  });

  it('shows the rate-limit message, rounding a short wait up to one minute', async () => {
    await render();
    await fill('someone', 'x');

    const done = submit();
    flushProblem(http.expectOne('/api/auth/login'), 429, 'error.auth.rate_limited', { headers: { 'Retry-After': '20' } });
    await done;
    await fixture.whenStable();

    expect(alertText()).toContain(ar.error.auth.rate_limited);
    expect(alertText()).toContain(ar.login.retry_after.replace('{{ minutes }}', '1'));
  });

  it('shows a translated network message with no raw key', async () => {
    await render();
    await fill('someone', 'x');

    const done = submit();
    http.expectOne('/api/auth/login').error(new ProgressEvent('error'));
    await done;
    await fixture.whenStable();

    expect(alertText()).toContain(ar.error.network);
    expect(alertText()).not.toContain('error.network');
  });

  it('never shows server English or a raw key for an unknown error, only the correlation id to quote', async () => {
    await render();
    await fill('someone', 'x');

    const done = submit();
    flushProblem(http.expectOne('/api/auth/login'), 500, 'Internal Server Error');
    await done;
    await fixture.whenStable();

    expect(alertText()).toContain(ar.error.unexpected);
    expect(alertText()).not.toContain('Internal Server Error');
    expect(alertText()).toContain('corr-123');
  });

  it('falls back to the unexpected message for a well-formed key nobody translated', async () => {
    await render();
    await fill('someone', 'x');

    const done = submit();
    flushProblem(http.expectOne('/api/auth/login'), 400, 'error.brand.new_thing');
    await done;
    await fixture.whenStable();

    expect(alertText()).toContain(ar.error.unexpected);
    expect(alertText()).not.toContain('error.brand.new_thing');
  });

  it('shows the session-expired notice once, after a session ended mid-use', async () => {
    await render();
    const session = TestBed.inject(SessionService);

    session.expire();
    await fixture.whenStable();
    expect(root().querySelector('[role=status]')?.textContent?.trim()).toBe(ar.login.expired);

    await fill('someone', 'x');
    const done = submit();
    flushProblem(http.expectOne('/api/auth/login'), 401, 'error.auth.invalid_credentials');
    await done;
    await fixture.whenStable();
    expect(root().querySelector('[role=status]')).toBeNull();
  });
});
