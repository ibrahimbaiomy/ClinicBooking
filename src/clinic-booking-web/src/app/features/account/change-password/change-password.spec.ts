import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { flushProblem, meBody, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import {
  accountArabic as aar,
  accountEnglish as aen,
  arabicTranslations as ar,
} from '../../../../testing/transloco-testing';
import { SessionService } from '../../../core/auth/session.service';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { ChangePassword, PASSWORD_MAX_LENGTH } from './change-password';

const ROUTES: Routes = [
  { path: 'change-password', component: ChangePassword },
  { path: '**', component: Stub },
];

const URL = '/api/auth/change-password';
const ME = '/api/auth/me';

// Distinct, recognisable values: a leak anywhere is found by searching for them.
const CURRENT = 'Current-Secret-Value-1';
const NEXT = 'Brand-New-Secret-Value-2';
const OTHER = 'Another-Secret-Value-3';

describe('ChangePassword page (D58, D59)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const input = (id: string) => one<HTMLInputElement>(`#${id}`)!;
  const current = () => input('cp-current');
  const fresh = () => input('cp-new');
  const confirm = () => input('cp-confirm');

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  async function open(url = '/change-password', options: { forced?: boolean; language?: 'ar' | 'en' } = {}): Promise<void> {
    const language = options.language ?? 'ar';
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    await signIn([], 'token-1', { mustChangePassword: options.forced ?? false });
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load(`account/${language}`);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, ChangePassword);
    await settle();
  }

  async function type(id: string, value: string): Promise<void> {
    input(id).value = value;
    input(id).dispatchEvent(new Event('input'));
    await settle();
  }

  async function fill(values: { current?: string; next?: string; confirm?: string } = {}): Promise<void> {
    await type('cp-current', values.current ?? CURRENT);
    await type('cp-new', values.next ?? NEXT);
    await type('cp-confirm', values.confirm ?? values.next ?? NEXT);
  }

  async function submit(): Promise<void> {
    one<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  /** The request goes out; the test then answers it. */
  async function answer(respond: (request: ReturnType<HttpTestingController['expectOne']>) => void): Promise<void> {
    respond(http.expectOne(URL));
    await settle();
  }

  /** Nothing about any password may be anywhere a later reader could find it. */
  function expectNoPasswordAnywhere(): void {
    const everything = [
      root().innerHTML,
      router.url,
      location.href,
      JSON.stringify(history.state),
      JSON.stringify(router.lastSuccessfulNavigation()?.extras ?? {}),
      JSON.stringify({ ...localStorage }),
      JSON.stringify({ ...sessionStorage }),
    ].join('\n');
    for (const secret of [CURRENT, NEXT, OTHER]) {
      expect(everything).not.toContain(secret);
    }
    // After a successful forced change the page is gone; otherwise the fields are there and empty.
    for (const id of ['cp-current', 'cp-new', 'cp-confirm']) {
      const field = one<HTMLInputElement>(`#${id}`);
      if (field !== null) {
        expect(field.value).toBe('');
      }
    }
  }

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('account')],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  describe('the form', () => {
    it('has labelled fields, the right autocomplete values and a policy hint (D48)', async () => {
      await open();

      expect(text('h2')).toBe(aar.title);
      expect(one('label[for=cp-current]')?.textContent?.trim()).toBe(aar.current);
      expect(one('label[for=cp-new]')?.textContent?.trim()).toBe(aar.new);
      expect(one('label[for=cp-confirm]')?.textContent?.trim()).toBe(aar.confirm);
      expect(current().getAttribute('autocomplete')).toBe('current-password');
      expect(fresh().getAttribute('autocomplete')).toBe('new-password');
      expect(confirm().getAttribute('autocomplete')).toBe('new-password');
      expect(current().type).toBe('password');
      expect(text('#cp-new-hint')).toBe(aar.hint);
      expect(document.activeElement).toBe(one('h2'));
    });

    it('states the policy hint exactly as D48 (12 characters, upper, lower, digit) in English', async () => {
      await open('/change-password', { language: 'en' });

      expect(text('#cp-new-hint')).toBe(
        'At least 12 characters, with an upper-case letter, a lower-case letter and a digit.',
      );
    });

    it('shows the forced notice, and no "intro", for a user with a temporary password', async () => {
      await open('/change-password', { forced: true });

      expect(text('[role=note]')).toBe(aar.forced_notice);
      expect(root().textContent).not.toContain(aar.intro);
    });

    it('shows the intro, and no forced notice, for a voluntary change', async () => {
      await open();

      expect(one('[role=note]')).toBeNull();
      expect(root().textContent).toContain(aar.intro);
    });

    it('works in English too', async () => {
      await open('/change-password', { language: 'en' });

      expect(text('h2')).toBe(aen.title);
      expect(one('label[for=cp-current]')?.textContent?.trim()).toBe(aen.current);
    });
  });

  describe('show and hide', () => {
    it('uses one pattern: a fixed label with aria-pressed, never a label that changes too', async () => {
      await open();
      const toggle = one<HTMLButtonElement>('button[aria-controls=cp-current]')!;

      expect(toggle.textContent?.trim()).toBe(aar.show_current);
      expect(toggle.getAttribute('aria-pressed')).toBe('false');
      expect(current().type).toBe('password');

      toggle.click();
      await settle();

      expect(toggle.textContent?.trim()).toBe(aar.show_current);
      expect(toggle.getAttribute('aria-pressed')).toBe('true');
      expect(current().type).toBe('text');

      toggle.click();
      await settle();

      expect(toggle.textContent?.trim()).toBe(aar.show_current);
      expect(toggle.getAttribute('aria-pressed')).toBe('false');
      expect(current().type).toBe('password');
    });

    it('the second toggle shows and hides the new password and its confirmation together', async () => {
      await open();
      const toggle = one<HTMLButtonElement>('button[aria-controls="cp-new cp-confirm"]')!;

      toggle.click();
      await settle();

      expect(toggle.textContent?.trim()).toBe(aar.show_new);
      expect(toggle.getAttribute('aria-pressed')).toBe('true');
      expect(fresh().type).toBe('text');
      expect(confirm().type).toBe('text');
    });

    it('the fields are hidden again after a request is sent', async () => {
      await open();
      await fill();
      one<HTMLButtonElement>('button[aria-controls=cp-current]')!.click();
      one<HTMLButtonElement>('button[aria-controls="cp-new cp-confirm"]')!.click();
      await settle();

      await submit();
      await answer((request) => request.flush(null, { status: 204, statusText: 'No Content' }));
      http.expectOne(ME).flush(meBody());
      await settle();

      expect(current().type).toBe('password');
      expect(fresh().type).toBe('password');
      expect(confirm().type).toBe('password');
    });
  });

  describe('client checks (the server stays the authority for the policy)', () => {
    it('requires all three fields with the back-end keys, focusing the first invalid, and sends nothing', async () => {
      await open();
      document.body.appendChild(root());

      await submit();

      expect(text('#cp-current-error')).toBe(ar.error.auth.current_password_required);
      expect(text('#cp-new-error')).toBe(ar.error.password.required);
      expect(document.activeElement).toBe(current());
      http.expectNone(URL);
      root().remove();
    });

    it(`refuses more than ${PASSWORD_MAX_LENGTH} characters in either field`, async () => {
      await open();
      await fill({ current: 'a'.repeat(PASSWORD_MAX_LENGTH + 1), next: 'b'.repeat(PASSWORD_MAX_LENGTH + 1) });

      await submit();

      expect(text('#cp-current-error')).toBe(ar.error.password.too_long);
      expect(text('#cp-new-error')).toBe(ar.error.password.too_long);
      http.expectNone(URL);
    });

    it('accepts exactly 128 characters', async () => {
      await open();
      await fill({ current: 'a'.repeat(PASSWORD_MAX_LENGTH), next: 'b'.repeat(PASSWORD_MAX_LENGTH) });

      await submit();

      http.expectOne(URL).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      http.expectOne(ME).flush(meBody());
    });

    it('requires the confirmation to equal the new password', async () => {
      await open();
      await fill({ next: NEXT, confirm: OTHER });

      await submit();

      expect(text('#cp-confirm-error')).toBe(aar.mismatch);
      http.expectNone(URL);
      // A client-side refusal is not a request: the typed text is still there to correct.
      expect(fresh().value).toBe(NEXT);
    });

    it('does not check the policy or "unchanged" in the client: a weak new password is sent', async () => {
      await open();
      await fill({ current: CURRENT, next: 'abc', confirm: 'abc' });

      await submit();

      http.expectOne(URL).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      http.expectOne(ME).flush(meBody());
    });
  });

  describe('the request', () => {
    it('sends exactly the current and the new password, and never the confirmation', async () => {
      await open();
      await fill();

      await submit();

      const request = http.expectOne(URL);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ currentPassword: CURRENT, newPassword: NEXT });
      request.flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      http.expectOne(ME).flush(meBody());
    });
  });

  describe('success', () => {
    it('forced: refreshes /me so the flag is false, then continues to the returnUrl', async () => {
      await open('/change-password?returnUrl=%2Fclinics', { forced: true });
      const session = TestBed.inject(SessionService);
      expect(session.mustChangePassword()).toBe(true);
      await fill();

      await submit();
      await answer((request) => request.flush(null, { status: 204, statusText: 'No Content' }));
      http.expectOne(ME).flush(meBody({ mustChangePassword: false }));
      await settle();

      expect(session.mustChangePassword()).toBe(false);
      expect(router.url).toBe('/clinics');
      expectNoPasswordAnywhere();
    });

    it('forced: goes to the home page when there is no returnUrl', async () => {
      await open('/change-password', { forced: true });
      await fill();

      await submit();
      await answer((request) => request.flush(null, { status: 204, statusText: 'No Content' }));
      http.expectOne(ME).flush(meBody());
      await settle();

      expect(router.url).toBe('/');
    });

    it.each(['https://evil.example/x', '//evil.example', '/login'])(
      'forced: an unsafe returnUrl (%s) is ignored and the user goes home',
      async (unsafe) => {
        await open(`/change-password?returnUrl=${encodeURIComponent(unsafe)}`, { forced: true });
        await fill();

        await submit();
        await answer((request) => request.flush(null, { status: 204, statusText: 'No Content' }));
        http.expectOne(ME).flush(meBody());
        await settle();

        expect(router.url).toBe('/');
      },
    );

    it('forced: still continues, and clears the flag locally, when /me cannot be read', async () => {
      await open('/change-password?returnUrl=%2Fclinics', { forced: true });
      const session = TestBed.inject(SessionService);
      await fill();

      await submit();
      await answer((request) => request.flush(null, { status: 204, statusText: 'No Content' }));
      flushProblem(http.expectOne(ME), 500, 'error.unexpected');
      await settle();

      expect(session.mustChangePassword()).toBe(false);
      expect(router.url).toBe('/clinics');
    });

    it('voluntary: stays on the page and says it worked, in a status region that takes the focus', async () => {
      await open();
      document.body.appendChild(root());
      await fill();

      await submit();
      await answer((request) => request.flush(null, { status: 204, statusText: 'No Content' }));
      http.expectOne(ME).flush(meBody());
      await settle();

      expect(router.url).toBe('/change-password');
      expect(text('[role=status]')).toBe(aar.success);
      expect(document.activeElement).toBe(one('[role=status]'));
      expectNoPasswordAnywhere();
      root().remove();
    });

    it('voluntary: the success message goes away when the next change starts', async () => {
      await open();
      await fill();
      await submit();
      await answer((request) => request.flush(null, { status: 204, statusText: 'No Content' }));
      http.expectOne(ME).flush(meBody());
      await settle();
      expect(text('[role=status]')).toBe(aar.success);

      await fill({ current: NEXT, next: OTHER });
      await submit();

      expect(text('[role=status]')).toBe('');
      http.expectOne(URL).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      http.expectOne(ME).flush(meBody());
    });
  });

  describe('server errors', () => {
    it('a wrong current password goes on the current field, the form is emptied, the field takes the focus', async () => {
      await open();
      document.body.appendChild(root());
      await fill();

      await submit();
      await answer((request) =>
        flushProblem(request, 400, 'error.auth.current_password_incorrect', {
          body: { errors: { currentPassword: ['error.auth.current_password_incorrect'] } },
        }),
      );

      expect(text('#cp-current-error')).toBe(ar.error.auth.current_password_incorrect);
      expect(text('#cp-new-error')).toBe('');
      expect(document.activeElement).toBe(current());
      expect(router.url).toBe('/change-password');
      expectNoPasswordAnywhere();
      root().remove();
    });

    it('the title key alone is enough to find the field (no errors object)', async () => {
      await open();
      await fill();

      await submit();
      await answer((request) => flushProblem(request, 400, 'error.auth.current_password_incorrect'));

      expect(text('#cp-current-error')).toBe(ar.error.auth.current_password_incorrect);
    });

    it('"unchanged" goes on the new password field', async () => {
      await open();
      await fill({ current: CURRENT, next: CURRENT });

      await submit();
      await answer((request) =>
        flushProblem(request, 400, 'error.auth.password_unchanged', {
          body: { errors: { newPassword: ['error.auth.password_unchanged'] } },
        }),
      );

      expect(text('#cp-new-error')).toBe(ar.error.auth.password_unchanged);
    });

    it('every policy key the server sends is shown on the new password field', async () => {
      await open();
      await fill({ next: 'abc', confirm: 'abc' });

      await submit();
      await answer((request) =>
        flushProblem(request, 400, 'error.validation.failed', {
          body: {
            errors: {
              newPassword: ['error.password.too_short', 'error.password.requires_digit', 'error.password.requires_uppercase'],
            },
          },
        }),
      );

      const shown = text('#cp-new-error');
      expect(shown).toContain(ar.error.password.too_short);
      expect(shown).toContain(ar.error.password.requires_digit);
      expect(shown).toContain(ar.error.password.requires_uppercase);
      expect(shown).not.toContain(ar.error.password.requires_lowercase);
    });

    it('a server message that is not a known key is replaced by "unexpected" (never raw text)', async () => {
      await open();
      await fill();

      await submit();
      await answer((request) =>
        flushProblem(request, 400, 'error.validation.failed', {
          body: { errors: { newPassword: ['error.password.not_a_real_key'] } },
        }),
      );

      expect(text('#cp-new-error')).toBe(ar.error.unexpected);
    });

    it('typing in a field clears the server message of that field only', async () => {
      await open();
      await fill();
      await submit();
      await answer((request) =>
        flushProblem(request, 400, 'error.auth.current_password_incorrect', {
          body: { errors: { currentPassword: ['error.auth.current_password_incorrect'] } },
        }),
      );
      expect(text('#cp-current-error')).not.toBe('');

      await type('cp-new', OTHER);
      expect(text('#cp-current-error')).not.toBe('');

      await type('cp-current', CURRENT);
      expect(text('#cp-current-error')).toBe('');
    });

    it('423 shows the message and "about N minutes" from Retry-After, in the alert region', async () => {
      await open();
      await fill();

      await submit();
      await answer((request) => flushProblem(request, 423, 'error.auth.locked_out', { headers: { 'Retry-After': '600' } }));

      expect(text('[role=alert]')).toContain(ar.error.auth.locked_out);
      expect(text('[role=alert]')).toContain(aar.retry_after.replace('{{ minutes }}', '10'));
      expect(document.activeElement).toBe(one('[role=alert]'));
      expectNoPasswordAnywhere();
    });

    it('429 is shown the same way', async () => {
      await open();
      await fill();

      await submit();
      await answer((request) => flushProblem(request, 429, 'error.auth.rate_limited', { headers: { 'Retry-After': '30' } }));

      expect(text('[role=alert]')).toContain(ar.error.auth.rate_limited);
      expect(text('[role=alert]')).toContain(aar.retry_after.replace('{{ minutes }}', '1'));
    });

    it('an unexplained failure shows "unexpected" and a reference to quote', async () => {
      await open();
      await fill();

      await submit();
      await answer((request) => flushProblem(request, 500, 'error.unexpected'));

      expect(text('[role=alert]')).toContain(ar.error.unexpected);
      expect(text('[role=alert]')).toContain('corr-123');
      expectNoPasswordAnywhere();
    });

    it('a network failure is shown as such, and the form is still emptied', async () => {
      await open();
      await fill();

      await submit();
      await answer((request) => request.error(new ProgressEvent('error'), { status: 0 }));

      expect(text('[role=alert]')).toContain(ar.error.network);
      expectNoPasswordAnywhere();
    });

    it('after a failure the user can try again with the same page', async () => {
      await open();
      await fill();
      await submit();
      await answer((request) => flushProblem(request, 500, 'error.unexpected'));

      await fill({ current: CURRENT, next: NEXT });
      await submit();

      const request = http.expectOne(URL);
      expect(request.request.body).toEqual({ currentPassword: CURRENT, newPassword: NEXT });
      request.flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      http.expectOne(ME).flush(meBody());
    });
  });

  describe('passwords never outlive the form', () => {
    it('after a successful submit no password is in the DOM, the URL, the router state or storage', async () => {
      await open('/change-password?returnUrl=%2Fclinics', { forced: true });
      document.body.appendChild(root());
      await fill();
      // While typing, the DOM holds the values as input properties only; after the request they are gone.

      await submit();
      await answer((request) => request.flush(null, { status: 204, statusText: 'No Content' }));
      http.expectOne(ME).flush(meBody());
      await settle();

      expectNoPasswordAnywhere();
      root().remove();
    });

    it('while the request is pending the fields are already empty', async () => {
      await open();
      await fill();

      await submit();

      expect(current().value).toBe('');
      expect(fresh().value).toBe('');
      expect(confirm().value).toBe('');
      http.expectOne(URL).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      http.expectOne(ME).flush(meBody());
    });
  });
});
