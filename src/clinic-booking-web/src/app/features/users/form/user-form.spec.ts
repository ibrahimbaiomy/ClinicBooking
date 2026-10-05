import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { arabicTranslations as ar, usersArabic as uar } from '../../../../testing/transloco-testing';
import { detail, USERS_URL } from '../../../../testing/users-testing';
import { LanguageService } from '../../../core/i18n/language.service';
import { UsersSession } from '../users-session';
import { PASSWORD_MAX_LENGTH, USER_NAME_MAX_LENGTH, UserForm } from './user-form';

const ROUTES: Routes = [
  { path: 'users/new', component: UserForm },
  { path: '**', component: Stub },
];

// A recognisable value: a leak anywhere is found by searching for it.
const SECRET = 'Temp-Secret-Value-123';

describe('UserForm (create a user, D57, D59)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const nameField = () => one<HTMLInputElement>('#user-name')!;
  const passwordField = () => one<HTMLInputElement>('#user-temporary-password')!;

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  async function open(): Promise<void> {
    await signIn(['users.manage']);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load('users/ar');
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/users/new', UserForm);
    await settle();
  }

  async function type(field: HTMLInputElement, value: string): Promise<void> {
    field.value = value;
    field.dispatchEvent(new Event('input'));
    await settle();
  }

  async function fill(userName = 'new.user', password = SECRET): Promise<void> {
    await type(nameField(), userName);
    await type(passwordField(), password);
  }

  async function submit(): Promise<void> {
    one<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

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
    expect(everything).not.toContain(SECRET);
    const field = one<HTMLInputElement>('#user-temporary-password');
    if (field !== null) {
      expect(field.value).toBe('');
    }
  }

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('users')] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  describe('the form', () => {
    beforeEach(() => open());

    it('has labelled fields, hints, autocomplete off for the name and new-password for the password', () => {
      expect(text('h2')).toBe(uar.form.title);
      expect(one('label[for=user-name]')?.textContent?.trim()).toBe(uar.form.user_name);
      expect(one('label[for=user-temporary-password]')?.textContent?.trim()).toBe(uar.form.temporary_password);
      expect(text('#user-name-hint')).toBe(uar.form.user_name_hint);
      expect(text('#user-password-hint')).toBe(uar.form.temporary_password_hint);
      expect(nameField().getAttribute('autocomplete')).toBe('off');
      expect(nameField().getAttribute('dir')).toBe('ltr');
      expect(passwordField().getAttribute('autocomplete')).toBe('new-password');
      expect(passwordField().type).toBe('password');
      expect(document.activeElement).toBe(one('h2'));
    });

    it('the toggle is one pattern: a fixed label with aria-pressed', async () => {
      const toggle = one<HTMLButtonElement>('button[aria-controls=user-temporary-password]')!;
      expect(toggle.textContent?.trim()).toBe(uar.form.show_password);
      expect(toggle.getAttribute('aria-pressed')).toBe('false');

      toggle.click();
      await settle();

      expect(toggle.textContent?.trim()).toBe(uar.form.show_password);
      expect(toggle.getAttribute('aria-pressed')).toBe('true');
      expect(passwordField().type).toBe('text');
    });

    it('Cancel goes back to the list', () => {
      expect(one<HTMLAnchorElement>('a[href="/users"]')?.textContent?.trim()).toBe(uar.form.cancel);
    });
  });

  describe('client checks mirror the server', () => {
    beforeEach(() => open());

    it('requires both fields (a blank name too) with the back-end keys, focusing the first invalid', async () => {
      document.body.appendChild(root());
      await type(nameField(), '   ');

      await submit();

      expect(text('#user-name-error')).toBe(ar.error.user.user_name_required);
      expect(text('#user-password-error')).toBe(ar.error.password.required);
      expect(document.activeElement).toBe(nameField());
      http.expectNone(USERS_URL);
      root().remove();
    });

    it.each([
      ['ab', 'user_name_too_short'],
      ['a'.repeat(USER_NAME_MAX_LENGTH + 1), 'user_name_too_long'],
      ['has space', 'user_name_invalid'],
      ['name@host', 'user_name_invalid'],
      ['اسم_عربي', 'user_name_invalid'],
    ] as const)('user name %j gives error.user.%s and sends nothing', async (name, key) => {
      await fill(name);

      await submit();

      expect(text('#user-name-error')).toBe(ar.error.user[key]);
      http.expectNone(USERS_URL);
    });

    it.each(['abc', 'a.b-c_d', 'x'.repeat(USER_NAME_MAX_LENGTH)])('accepts the user name %j', async (name) => {
      await fill(name);

      await submit();

      http.expectOne(USERS_URL).flush(detail(5, name, { mustChangePassword: true }), { status: 201, statusText: 'Created' });
      await settle();
    });

    it(`refuses a password over ${PASSWORD_MAX_LENGTH} characters, and does not judge the policy itself`, async () => {
      await fill('someone', 'p'.repeat(PASSWORD_MAX_LENGTH + 1));
      await submit();
      expect(text('#user-password-error')).toBe(ar.error.password.too_long);
      http.expectNone(USERS_URL);

      await type(passwordField(), 'abc'); // far too weak: the server decides
      await submit();
      http.expectOne(USERS_URL).flush(detail(5, 'someone'), { status: 201, statusText: 'Created' });
      await settle();
    });
  });

  describe('success', () => {
    beforeEach(() => open());

    it('creates the user, remembers a one-time message, and opens the new user', async () => {
      await fill('new.user');

      await submit();

      const request = http.expectOne(USERS_URL);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ userName: 'new.user', temporaryPassword: SECRET });
      request.flush(detail(42, 'new.user', { mustChangePassword: true }), { status: 201, statusText: 'Created' });
      await settle();

      expect(router.url).toBe('/users/42');
      expect(TestBed.inject(UsersSession).takeFlash()).toBe('users.flash.created');
      expectNoPasswordAnywhere();
    });

    it('works with an id that comes as a string (int64 typing)', async () => {
      await fill();

      await submit();
      http.expectOne(USERS_URL).flush(detail('42', 'new.user'), { status: 201, statusText: 'Created' });
      await settle();

      expect(router.url).toBe('/users/42');
    });

    it('the password field is already empty while the request is pending', async () => {
      await fill();

      await submit();

      expect(passwordField().value).toBe('');
      expect(nameField().value).toBe('new.user');
      http.expectOne(USERS_URL).flush(detail(5, 'new.user'), { status: 201, statusText: 'Created' });
      await settle();
    });

    it('the typed password is nowhere after the submit: not in the DOM, the URL, the router state or storage', async () => {
      document.body.appendChild(root());
      await fill();
      one<HTMLButtonElement>('button[aria-controls=user-temporary-password]')!.click(); // shown while typing
      await settle();

      await submit();

      expectNoPasswordAnywhere();
      expect(passwordField().type).toBe('password');
      http.expectOne(USERS_URL).flush(detail(5, 'new.user'), { status: 201, statusText: 'Created' });
      await settle();
      expectNoPasswordAnywhere();
      root().remove();
    });
  });

  describe('server errors', () => {
    beforeEach(() => open());

    it('409 user name taken goes on the user name, keeps the name, empties the password and focuses the name', async () => {
      document.body.appendChild(root());
      await fill('taken.name');

      await submit();
      flushProblem(http.expectOne(USERS_URL), 409, 'error.user.user_name_taken');
      await settle();

      expect(text('#user-name-error')).toBe(ar.error.user.user_name_taken);
      expect(nameField().value).toBe('taken.name');
      expect(passwordField().value).toBe('');
      expect(document.activeElement).toBe(nameField());
      expect(router.url).toBe('/users/new');
      expectNoPasswordAnywhere();
      root().remove();
    });

    it('policy keys go on the password field, all of them', async () => {
      await fill('someone', 'abc');

      await submit();
      flushProblem(http.expectOne(USERS_URL), 400, 'error.validation.failed', {
        body: {
          errors: {
            temporaryPassword: ['error.password.too_short', 'error.password.requires_digit', 'error.password.requires_uppercase'],
          },
        },
      });
      await settle();

      const shown = text('#user-password-error');
      expect(shown).toContain(ar.error.password.too_short);
      expect(shown).toContain(ar.error.password.requires_digit);
      expect(shown).toContain(ar.error.password.requires_uppercase);
      expect(passwordField().value).toBe('');
    });

    it('user name keys from a 400 go on the user name', async () => {
      await fill('someone');

      await submit();
      flushProblem(http.expectOne(USERS_URL), 400, 'error.validation.failed', {
        body: { errors: { userName: ['error.user.user_name_invalid'] } },
      });
      await settle();

      expect(text('#user-name-error')).toBe(ar.error.user.user_name_invalid);
    });

    it('an unknown server message becomes "unexpected", never raw text', async () => {
      await fill('someone');

      await submit();
      flushProblem(http.expectOne(USERS_URL), 400, 'error.validation.failed', {
        body: { errors: { userName: ['Some English sentence'] } },
      });
      await settle();

      expect(text('#user-name-error')).toBe(ar.error.validation.invalid);
      expect(root().textContent).not.toContain('Some English sentence');
    });

    it('typing in a field clears its server message only', async () => {
      await fill('taken.name');
      await submit();
      flushProblem(http.expectOne(USERS_URL), 409, 'error.user.user_name_taken');
      await settle();

      await type(passwordField(), SECRET);
      expect(text('#user-name-error')).toBe(ar.error.user.user_name_taken);

      await type(nameField(), 'other.name');
      expect(text('#user-name-error')).toBe('');
    });

    it('anything else is a form-level message with a reference to quote, and the password is still empty', async () => {
      document.body.appendChild(root());
      await fill();

      await submit();
      flushProblem(http.expectOne(USERS_URL), 500, 'error.unexpected');
      await settle();

      expect(text('[role=alert]')).toContain(ar.error.unexpected);
      expect(text('[role=alert]')).toContain('corr-123');
      expectNoPasswordAnywhere();
      root().remove();
    });

    it('a network failure is shown as such', async () => {
      await fill();

      await submit();
      http.expectOne(USERS_URL).error(new ProgressEvent('error'), { status: 0 });
      await settle();

      expect(text('[role=alert]')).toContain(ar.error.network);
    });

    it('after a failure the form can be submitted again', async () => {
      await fill('someone');
      await submit();
      flushProblem(http.expectOne(USERS_URL), 500, 'error.unexpected');
      await settle();

      await type(passwordField(), SECRET);
      await submit();

      const request = http.expectOne(USERS_URL);
      expect(request.request.body).toEqual({ userName: 'someone', temporaryPassword: SECRET });
      request.flush(detail(5, 'someone'), { status: 201, statusText: 'Created' });
      await settle();
    });
  });
});
