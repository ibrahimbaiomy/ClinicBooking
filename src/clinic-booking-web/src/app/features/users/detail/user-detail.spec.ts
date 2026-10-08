import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { UserDetail } from '../../../../api/users-api';
import { flushProblem, meBody, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { installDialogPolyfill } from '../../../../testing/dialog-polyfill';
import {
  arabicTranslations as ar,
  usersArabic as uar,
  usersEnglish as uen,
} from '../../../../testing/transloco-testing';
import { ASSIGNABLE, clinicGrant, detail, PERMISSIONS_URL } from '../../../../testing/users-testing';
import { SessionService } from '../../../core/auth/session.service';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { UsersSession } from '../users-session';
import { PASSWORD_MAX_LENGTH, UserDetailPage } from './user-detail';

const ROUTES: Routes = [
  { path: 'users/:id', component: UserDetailPage },
  { path: '**', component: Stub },
];

const USER_URL = '/api/users/5';
const ME = '/api/auth/me';

// A recognisable value: a leak anywhere is found by searching for it.
const SECRET = 'Reset-Secret-Value-123';

const ANN = detail(5, 'ann', {
  globalPermissions: ['clinics.manage'],
  clinicPermissions: [clinicGrant(9, 'عيادة النيل', 'Nile Clinic', ['doctors.manage'])],
});

describe('UserDetailPage (D57, D58, D59)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;
  let session: SessionService;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const all = <T extends Element>(selector: string) => [...root().querySelectorAll<T>(selector)];
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const button = (label: string) => all<HTMLButtonElement>('button').find((b) => b.textContent?.trim() === label);
  const dialogs = () => all<HTMLDialogElement>('dialog');
  const disableDialog = () => dialogs()[0];
  const resetDialog = () => dialogs()[1];
  const dialogButtons = (dialog: HTMLDialogElement) => [...dialog.querySelectorAll<HTMLButtonElement>(':scope > div:last-of-type button')];
  const resetField = () => one<HTMLInputElement>('#reset-password')!;

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  /** Opens /users/5 and answers the two requests the page makes. */
  async function open(
    user: UserDetail = ANN,
    options: { meId?: number | string; language?: 'ar' | 'en'; me?: Partial<ReturnType<typeof meBody>> } = {},
  ): Promise<void> {
    const language = options.language ?? 'ar';
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    await signIn(['users.manage'], 'token-1', { id: options.meId ?? 1, ...options.me });
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load(`users/${language}`);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/users/5', UserDetailPage);
    await settle();
    http.expectOne(USER_URL).flush(user);
    http.expectOne(PERMISSIONS_URL).flush(ASSIGNABLE);
    await settle();
  }

  beforeEach(() => {
    installDialogPolyfill();
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('users')] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    session = TestBed.inject(SessionService);
  });

  afterEach(() => http.verify());

  describe('the summary', () => {
    it('shows the user name left to right, the status in words, the password indicator and the date', async () => {
      await open(detail(5, 'ann', { mustChangePassword: true }));

      expect(text('h2')).toBe(uar.detail.title);
      expect(one('dl bdi')!.getAttribute('dir')).toBe('ltr');
      expect(text('dl')).toContain('ann');
      expect(text('dl')).toContain(uar.status.active);
      expect(text('dl')).toContain(uar.status.must_change);
      expect(text('dl')).not.toContain(uar.status.disabled);
    });

    it('a user who has set their own password shows that instead of the indicator', async () => {
      await open();

      expect(text('dl')).toContain(uar.detail.password_ok);
      expect(text('dl')).not.toContain(uar.status.must_change);
    });

    it('a disabled user says so in words, and offers Enable instead of Disable', async () => {
      await open(detail(5, 'ann', { isActive: false }));

      expect(text('dl')).toContain(uar.status.disabled);
      expect(button(uar.detail.enable.action)).toBeDefined();
      expect(button(uar.detail.disable.action)).toBeUndefined();
    });

    it('shows the one-time message left by the page that opened it, in a status region', async () => {
      TestBed.inject(UsersSession).setFlash('users.flash.created');

      await open();

      expect(text('[role=status]')).toBe(uar.flash.created);
    });

    it('takes the focus on the heading on entry', async () => {
      document.body.appendChild(root());
      await open();

      expect(document.activeElement).toBe(one('h2'));
      root().remove();
    });

    it('works in English', async () => {
      await open(ANN, { language: 'en' });

      expect(text('h2')).toBe(uen.detail.title);
      expect(text('dl')).toContain(uen.status.active);
    });
  });

  describe('the states', () => {
    async function openAndFail(status: number, key: string): Promise<void> {
      await signIn(['users.manage']);
      await TestBed.inject(LanguageService).load();
      await TestBed.inject(TranslocoService).load('users/ar');
      harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/users/5', UserDetailPage);
      await settle();
      flushProblem(http.expectOne(USER_URL), status, key);
      http.expectOne(PERMISSIONS_URL).flush(ASSIGNABLE);
      await settle();
    }

    it('shows loading until the user arrives', async () => {
      await signIn(['users.manage']);
      await TestBed.inject(LanguageService).load();
      await TestBed.inject(TranslocoService).load('users/ar');
      harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/users/5', UserDetailPage);
      await settle();

      expect(text('[aria-busy]')).toBe(uar.loading);
      http.expectOne(USER_URL).flush(ANN);
      http.expectOne(PERMISSIONS_URL).flush(ASSIGNABLE);
    });

    it('404 shows "user not found" with a way back to the list', async () => {
      await openAndFail(404, 'error.user.not_found');

      expect(text('[role=alert]')).toContain(uar.detail.not_found.title);
      expect(one<HTMLAnchorElement>('[role=alert] a')!.getAttribute('href')).toBe('/users');
      expect(one('cb-global-permissions-editor')).toBeNull();
    });

    it('an id that is not a number is "not found" without any request', async () => {
      await signIn(['users.manage']);
      await TestBed.inject(LanguageService).load();
      await TestBed.inject(TranslocoService).load('users/ar');
      harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/users/abc', UserDetailPage);
      await settle();

      expect(text('[role=alert]')).toContain(uar.detail.not_found.title);
      http.expectNone(() => true);
    });

    it('another error shows a translated message and Retry, which asks again', async () => {
      await openAndFail(500, 'error.unexpected');
      expect(text('[role=alert]')).toContain(ar.error.unexpected);

      one<HTMLButtonElement>('[role=alert] button')!.click();
      await settle();
      http.expectOne(USER_URL).flush(ANN);
      http.expectOne(PERMISSIONS_URL).flush(ASSIGNABLE);
      await settle();

      expect(text('dl')).toContain('ann');
    });

    it('a user with no permissions at all shows both editors empty, with explicit "none" lines', async () => {
      await open(detail(5, 'ann'));

      expect(text('cb-global-permissions-editor')).toContain(uar.global.none);
      expect(text('cb-clinic-permissions-editor')).toContain(uar.clinics.none);
      expect(all<HTMLInputElement>('cb-global-permissions-editor input:checked')).toHaveLength(0);
    });

    it('the permission lists failing to load shows an error with Retry, while the summary stays', async () => {
      await signIn(['users.manage']);
      await TestBed.inject(LanguageService).load();
      await TestBed.inject(TranslocoService).load('users/ar');
      harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/users/5', UserDetailPage);
      await settle();
      http.expectOne(USER_URL).flush(ANN);
      flushProblem(http.expectOne(PERMISSIONS_URL), 500, 'error.unexpected');
      await settle();

      expect(text('dl')).toContain('ann');
      expect(root().textContent).toContain(ar.error.unexpected);

      button(uar.retry)!.click();
      await settle();
      http.expectOne(PERMISSIONS_URL).flush(ASSIGNABLE);
      await settle();
      expect(one('cb-global-permissions-editor')).not.toBeNull();
    });
  });

  describe('your own account', () => {
    it.each([
      [5, 5],
      [5, '5'],
      ['5', 5],
      ['5', '5'],
    ])('is recognised when your id is %j and the page says %j (int64 typing), and offers no disable or reset', async (own, shown) => {
      await open(detail(shown, 'ann', { globalPermissions: ['users.manage'] }), { meId: own });

      expect(text('main, section')).toContain(uar.detail.own_account);
      expect(button(uar.detail.disable.action)).toBeUndefined();
      expect(button(uar.detail.enable.action)).toBeUndefined();
      expect(button(uar.detail.reset.action)).toBeUndefined();
    });

    it("somebody else's account has both actions", async () => {
      await open(ANN, { meId: 99 });

      expect(button(uar.detail.disable.action)).toBeDefined();
      expect(button(uar.detail.reset.action)).toBeDefined();
      expect(root().textContent).not.toContain(uar.detail.own_account);
    });
  });

  describe('disable and enable', () => {
    it('Disable opens a dialog that names the user, with the focus on Cancel; Cancel sends nothing', async () => {
      await open();

      button(uar.detail.disable.action)!.click();
      await settle();

      expect(disableDialog().hasAttribute('open')).toBe(true);
      expect(disableDialog().textContent).toContain(uar.detail.disable.message.replace('{{ name }}', 'ann'));
      expect(document.activeElement).toBe(dialogButtons(disableDialog())[0]);

      dialogButtons(disableDialog())[0].click();
      await settle();

      expect(disableDialog().hasAttribute('open')).toBe(false);
      http.expectNone(`${USER_URL}/disable`);
    });

    it('confirming disables, shows the server answer and a message, and offers Enable', async () => {
      await open();
      button(uar.detail.disable.action)!.click();
      await settle();

      dialogButtons(disableDialog())[1].click();
      await settle();
      const request = http.expectOne(`${USER_URL}/disable`);
      expect(request.request.method).toBe('POST');
      request.flush({ ...ANN, isActive: false });
      await settle();

      expect(disableDialog().hasAttribute('open')).toBe(false);
      expect(text('dl')).toContain(uar.status.disabled);
      expect(text('[role=status]')).toBe(uar.flash.disabled);
      expect(button(uar.detail.enable.action)).toBeDefined();
    });

    it('the last-administrator refusal is shown clearly inside the dialog, which stays open', async () => {
      await open();
      button(uar.detail.disable.action)!.click();
      await settle();

      dialogButtons(disableDialog())[1].click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/disable`), 422, 'error.user.last_administrator');
      await settle();

      expect(disableDialog().hasAttribute('open')).toBe(true);
      expect(disableDialog().querySelector('[role=alert]')!.textContent).toContain(ar.error.user.last_administrator);
      expect(text('dl')).toContain(uar.status.active);
    });

    it('the self-disable refusal is translated too, if the server sends it', async () => {
      await open();
      button(uar.detail.disable.action)!.click();
      await settle();

      dialogButtons(disableDialog())[1].click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/disable`), 422, 'error.user.cannot_disable_self');
      await settle();

      expect(disableDialog().querySelector('[role=alert]')!.textContent).toContain(ar.error.user.cannot_disable_self);
    });

    it('Enable sends the request at once, shows the server answer and a message', async () => {
      await open(detail(5, 'ann', { isActive: false }));

      button(uar.detail.enable.action)!.click();
      await settle();
      const request = http.expectOne(`${USER_URL}/enable`);
      expect(request.request.method).toBe('POST');
      request.flush({ ...ANN, isActive: true });
      await settle();

      expect(text('dl')).toContain(uar.status.active);
      expect(text('[role=status]')).toBe(uar.flash.enabled);
      expect(button(uar.detail.disable.action)).toBeDefined();
    });

    it('a failed Enable shows a translated message', async () => {
      await open(detail(5, 'ann', { isActive: false }));

      button(uar.detail.enable.action)!.click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/enable`), 404, 'error.user.not_found');
      await settle();

      expect(text('[role=alert]')).toContain(ar.error.user.not_found);
    });
  });

  describe('reset password', () => {
    async function openReset(): Promise<void> {
      await open();
      button(uar.detail.reset.action)!.click();
      await settle();
    }

    async function typeSecret(value = SECRET): Promise<void> {
      resetField().value = value;
      resetField().dispatchEvent(new Event('input'));
      await settle();
    }

    function expectNoSecretAnywhere(): void {
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
      expect(resetField().value).toBe('');
    }

    it('opens a dialog with the focus on the password field, not on Cancel (an input dialog)', async () => {
      await openReset();

      expect(resetDialog().hasAttribute('open')).toBe(true);
      expect(resetDialog().textContent).toContain(uar.detail.reset.message.replace('{{ name }}', 'ann'));
      expect(document.activeElement).toBe(resetField());
      expect(resetField().getAttribute('autocomplete')).toBe('new-password');
      expect(resetField().type).toBe('password');
      expect(one('label[for=reset-password]')!.textContent?.trim()).toBe(uar.detail.reset.password);
    });

    it('the show toggle is one pattern: a fixed label with aria-pressed', async () => {
      await openReset();
      const toggle = one<HTMLButtonElement>('button[aria-controls=reset-password]')!;

      toggle.click();
      await settle();

      expect(toggle.textContent?.trim()).toBe(uar.detail.reset.show_password);
      expect(toggle.getAttribute('aria-pressed')).toBe('true');
      expect(resetField().type).toBe('text');
    });

    it('confirming sends the temporary password as the body, empties the field at once, then closes with a message', async () => {
      await openReset();
      await typeSecret();

      dialogButtons(resetDialog())[1].click();
      await settle();

      expect(resetField().value).toBe('');
      const request = http.expectOne(`${USER_URL}/reset-password`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ temporaryPassword: SECRET });
      request.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      expect(resetDialog().hasAttribute('open')).toBe(false);
      expect(text('[role=status]')).toBe(uar.flash.password_reset);
      expectNoSecretAnywhere();
    });

    it('Cancel closes and the field is empty afterwards, also when the dialog is opened again', async () => {
      await openReset();
      await typeSecret();

      dialogButtons(resetDialog())[0].click();
      await settle();

      expect(resetDialog().hasAttribute('open')).toBe(false);
      expect(resetField().value).toBe('');
      http.expectNone(`${USER_URL}/reset-password`);

      button(uar.detail.reset.action)!.click();
      await settle();
      expect(resetField().value).toBe('');
      expectNoSecretAnywhere();
    });

    it('Esc closes the dialog and empties the field', async () => {
      await openReset();
      await typeSecret();

      resetDialog().dispatchEvent(new Event('cancel', { cancelable: true }));
      await settle();

      expect(resetDialog().hasAttribute('open')).toBe(false);
      expect(resetField().value).toBe('');
      expectNoSecretAnywhere();
    });

    it('a close by any other route (the dialog closing itself) empties the field too', async () => {
      await openReset();
      await typeSecret();

      resetDialog().close();
      await settle();

      expect(resetField().value).toBe('');
      expectNoSecretAnywhere();
    });

    it('an empty password is refused in the dialog, and nothing is sent', async () => {
      await openReset();

      dialogButtons(resetDialog())[1].click();
      await settle();

      expect(text('#reset-password-error')).toBe(ar.error.password.required);
      expect(resetDialog().hasAttribute('open')).toBe(true);
      http.expectNone(`${USER_URL}/reset-password`);
    });

    it(`refuses more than ${PASSWORD_MAX_LENGTH} characters`, async () => {
      await openReset();
      await typeSecret('p'.repeat(PASSWORD_MAX_LENGTH + 1));

      dialogButtons(resetDialog())[1].click();
      await settle();

      expect(text('#reset-password-error')).toBe(ar.error.password.too_long);
      http.expectNone(`${USER_URL}/reset-password`);
    });

    it('policy refusals from the server are all listed in the dialog, which stays open and empty', async () => {
      await openReset();
      await typeSecret('abc');

      dialogButtons(resetDialog())[1].click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/reset-password`), 400, 'error.validation.failed', {
        body: { errors: { temporaryPassword: ['error.password.too_short', 'error.password.requires_digit'] } },
      });
      await settle();

      expect(resetDialog().hasAttribute('open')).toBe(true);
      expect(text('#reset-password-error')).toContain(ar.error.password.too_short);
      expect(text('#reset-password-error')).toContain(ar.error.password.requires_digit);
      expect(resetField().value).toBe('');
    });

    it('typing clears the refusal', async () => {
      await openReset();
      dialogButtons(resetDialog())[1].click();
      await settle();
      expect(text('#reset-password-error')).not.toBe('');

      await typeSecret('a');

      expect(text('#reset-password-error')).toBe('');
    });

    it('the own-account refusal from the server is shown translated inside the dialog', async () => {
      await openReset();
      await typeSecret();

      dialogButtons(resetDialog())[1].click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/reset-password`), 422, 'error.user.cannot_reset_own_password');
      await settle();

      // The dialog's own error region (the field has its own alert, which is empty here).
      expect(resetDialog().querySelector(':scope > div[role=alert]')!.textContent).toContain(ar.error.user.cannot_reset_own_password);
      expect(resetField().value).toBe('');
    });

    it('a failure never leaves the typed password anywhere', async () => {
      await openReset();
      await typeSecret();

      dialogButtons(resetDialog())[1].click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/reset-password`), 500, 'error.unexpected');
      await settle();

      expectNoSecretAnywhere();
    });
  });

  describe('the global permissions editor', () => {
    const editor = () => one('cb-global-permissions-editor')!;
    const boxes = () => [...editor().querySelectorAll<HTMLInputElement>('input[type=checkbox]')];
    const save = () => [...editor().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === uar.global.save)!;

    it('lists the names the API sends, each with its label and description, the held ones checked', async () => {
      await open();

      expect(boxes()).toHaveLength(3);
      expect(boxes().map((box) => box.checked)).toEqual([false, false, true]);
      expect(editor().textContent).toContain(uar.permissions.users.manage.label);
      expect(editor().textContent).toContain(uar.permissions.users.manage.description);
      expect(editor().textContent).toContain(uar.permissions.clinics.manage.label);
      // The checkbox is labelled by its label, and described by the description.
      const first = boxes()[0];
      expect(editor().querySelector(`label[for=${first.id}]`)!.textContent).toBe(uar.permissions.users.manage.label);
      expect(first.getAttribute('aria-describedby')).toBeTruthy();
    });

    it('shows a bare name, left to right, for a permission that has no label yet', async () => {
      await signIn(['users.manage']);
      await TestBed.inject(LanguageService).load();
      await TestBed.inject(TranslocoService).load('users/ar');
      harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/users/5', UserDetailPage);
      await settle();
      http.expectOne(USER_URL).flush(ANN);
      http.expectOne(PERMISSIONS_URL).flush({ global: [...ASSIGNABLE.global, 'reports.view'], clinicScoped: [] });
      await settle();

      const bare = editor().querySelector('label[for="global-reports-view"] bdi')!;
      expect(bare.textContent).toBe('reports.view');
      expect(bare.getAttribute('dir')).toBe('ltr');
    });

    it('Save is off until something changes', async () => {
      await open();
      expect(save().disabled).toBe(true);

      boxes()[0].click();
      await settle();
      expect(save().disabled).toBe(false);

      boxes()[0].click();
      await settle();
      expect(save().disabled).toBe(true);
    });

    it('saves the full set in the API order, then reloads and shows the server state (last write wins)', async () => {
      await open();
      boxes()[1].click(); // specialties.manage
      boxes()[0].click(); // users.manage
      await settle();

      save().click();
      await settle();
      const put = http.expectOne(`${USER_URL}/global-permissions`);
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual({ permissions: ['users.manage', 'specialties.manage', 'clinics.manage'] });
      put.flush(ANN);
      await settle();
      // The reload answers with what the server really has, which differs from what was sent.
      http.expectOne(USER_URL).flush({ ...ANN, globalPermissions: ['specialties.manage'] });
      await settle();

      expect(boxes().map((box) => box.checked)).toEqual([false, true, false]);
      expect(text('[role=status]')).toBe(uar.flash.global_saved);
    });

    it('an empty set is a valid save', async () => {
      await open();
      boxes()[2].click(); // un-check clinics.manage
      await settle();

      save().click();
      await settle();

      const put = http.expectOne(`${USER_URL}/global-permissions`);
      expect(put.request.body).toEqual({ permissions: [] });
      put.flush(detail(5, 'ann'));
      await settle();
      http.expectOne(USER_URL).flush(detail(5, 'ann'));
      await settle();

      expect(boxes().every((box) => !box.checked)).toBe(true);
      expect(text('cb-global-permissions-editor')).toContain(uar.global.none);
    });

    it('falls back to the answer of the PUT when the reload fails', async () => {
      await open();
      boxes()[0].click();
      await settle();

      save().click();
      await settle();
      http.expectOne(`${USER_URL}/global-permissions`).flush({ ...ANN, globalPermissions: ['users.manage', 'clinics.manage'] });
      await settle();
      flushProblem(http.expectOne(USER_URL), 500, 'error.unexpected');
      await settle();

      expect(boxes().map((box) => box.checked)).toEqual([true, false, true]);
    });

    it('the last-administrator refusal is an alert in the editor, and the boxes stay as the user left them', async () => {
      await open(detail(5, 'ann', { globalPermissions: ['users.manage'] }), { meId: 5 });
      boxes()[0].click();
      await settle();

      save().click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/global-permissions`), 422, 'error.user.last_administrator');
      await settle();

      expect(text('cb-global-permissions-editor [role=alert]')).toContain(ar.error.user.last_administrator);
      expect(boxes()[0].checked).toBe(false);
      http.expectNone(USER_URL);
    });

    it('a keyed refusal shows no reference to quote; an unexpected failure does (D52)', async () => {
      await open(detail(5, 'ann', { globalPermissions: ['users.manage'] }), { meId: 5 });
      boxes()[0].click();
      await settle();

      save().click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/global-permissions`), 422, 'error.user.last_administrator');
      await settle();
      expect(text('cb-global-permissions-editor')).not.toContain('corr-123');

      save().click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/global-permissions`), 500, 'error.unexpected');
      await settle();
      expect(text('cb-global-permissions-editor [role=alert]')).toContain(ar.error.unexpected);
      expect(text('cb-global-permissions-editor')).toContain('corr-123');
    });

    it('a 404 is shown translated', async () => {
      await open();
      boxes()[0].click();
      await settle();

      save().click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/global-permissions`), 404, 'error.user.not_found');
      await settle();

      expect(text('cb-global-permissions-editor [role=alert]')).toContain(ar.error.user.not_found);
    });
  });

  describe('the clinic permissions editor', () => {
    const editor = () => one('cb-clinic-permissions-editor')!;
    const cards = () => [...editor().querySelectorAll<HTMLLIElement>(':scope > section > ul > li')];
    const cardButton = (card: HTMLElement, label: string) =>
      [...card.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === label)!;
    const boxesOf = (card: HTMLElement) => [...card.querySelectorAll<HTMLInputElement>('input[type=checkbox]')];

    it('lists each clinic with both names (the UI language first), and the names the API sends', async () => {
      await open();

      expect(cards()).toHaveLength(1);
      const names = [...cards()[0].querySelectorAll('legend bdi')];
      expect(names.map((n) => n.textContent)).toEqual(['عيادة النيل', 'Nile Clinic']);
      expect(names[0].getAttribute('lang')).toBe('ar');
      expect(names[1].getAttribute('dir')).toBe('ltr');
      expect(boxesOf(cards()[0]).map((box) => box.checked)).toEqual([true]);
      expect(cards()[0].textContent).toContain(uar.permissions.doctors.manage.label);
    });

    it('shows the English name first in the English UI', async () => {
      await open(ANN, { language: 'en' });

      expect([...cards()[0].querySelectorAll('legend bdi')].map((n) => n.textContent)).toEqual(['Nile Clinic', 'عيادة النيل']);
    });

    it('saving a clinic is a full replace for that clinic, then the detail is reloaded', async () => {
      await open();
      boxesOf(cards()[0])[0].click(); // un-check doctors.manage
      boxesOf(cards()[0])[0].click(); // and check it again: no change
      await settle();
      expect(cardButton(cards()[0], uar.clinics.save).disabled).toBe(true);

      boxesOf(cards()[0])[0].click();
      await settle();
      cardButton(cards()[0], uar.clinics.save).click();
      await settle();

      const put = http.expectOne(`${USER_URL}/clinics/9/permissions`);
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual({ permissions: [] });
      put.flush(detail(5, 'ann'));
      await settle();
      http.expectOne(USER_URL).flush(detail(5, 'ann', { globalPermissions: ['clinics.manage'] }));
      await settle();

      expect(cards()).toHaveLength(0); // the server no longer lists the clinic
      expect(text('[role=status]')).toBe(uar.flash.clinic_saved);
    });

    it('"Remove all" sends an empty set', async () => {
      await open();

      cardButton(cards()[0], uar.clinics.remove_all).click();
      await settle();

      const put = http.expectOne(`${USER_URL}/clinics/9/permissions`);
      expect(put.request.body).toEqual({ permissions: [] });
      put.flush(detail(5, 'ann'));
      await settle();
      http.expectOne(USER_URL).flush(detail(5, 'ann'));
      await settle();
      expect(cards()).toHaveLength(0);
    });

    it('a refusal for one clinic is shown on its card only', async () => {
      await open(
        detail(5, 'ann', {
          clinicPermissions: [
            clinicGrant(9, 'عيادة النيل', 'Nile Clinic', ['doctors.manage']),
            clinicGrant(10, 'عيادة النور', 'Al Noor Clinic', ['doctors.manage']),
          ],
        }),
      );

      cardButton(cards()[1], uar.clinics.remove_all).click();
      await settle();
      flushProblem(http.expectOne(`${USER_URL}/clinics/10/permissions`), 404, 'error.clinic.not_found');
      await settle();

      expect(cards()[1].querySelector('[role=alert]')!.textContent).toContain(ar.error.clinic.not_found);
      expect(cards()[0].querySelector('[role=alert]')!.textContent?.trim()).toBe('');
    });

    it('unsaved edits of another clinic survive a save of this one', async () => {
      await open(
        detail(5, 'ann', {
          clinicPermissions: [
            clinicGrant(9, 'عيادة النيل', 'Nile Clinic', ['doctors.manage']),
            clinicGrant(10, 'عيادة النور', 'Al Noor Clinic', ['doctors.manage']),
          ],
        }),
      );
      boxesOf(cards()[1])[0].click(); // an unsaved edit in the second clinic

      cardButton(cards()[0], uar.clinics.remove_all).click();
      await settle();
      http.expectOne(`${USER_URL}/clinics/9/permissions`).flush(detail(5, 'ann'));
      await settle();
      http.expectOne(USER_URL).flush(
        detail(5, 'ann', { clinicPermissions: [clinicGrant(10, 'عيادة النور', 'Al Noor Clinic', ['doctors.manage'])] }),
      );
      await settle();

      expect(cards()).toHaveLength(1);
      expect(boxesOf(cards()[0])[0].checked).toBe(false); // the edit is still there
    });

    describe('adding a clinic', () => {
      const picker = () => editor().querySelector('[role=search]')!.parentElement!;
      const search = () => editor().querySelector<HTMLInputElement>('#clinic-picker-search')!;
      const clinicRequest = () => http.expectOne((r) => r.url === '/api/clinics' && r.method === 'GET');
      const clinic = (id: number, nameAr: string, nameEn: string) => ({
        id,
        nameAr,
        nameEn,
        address: null,
        phone: null,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: null,
        rowVersion: 'AAAAAAAB',
      });
      const page = (items: ReturnType<typeof clinic>[], totalCount = items.length) => ({ items, page: 1, pageSize: 20, totalCount });

      async function openPicker(): Promise<TestRequest> {
        cardButton(editor() as HTMLElement, uar.clinics.add).click();
        await settle();
        return clinicRequest();
      }

      it('opens a search over the clinics list, the first 20 matches, newest nothing special', async () => {
        await open();

        const request = await openPicker();

        expect(request.request.params.get('Page')).toBe('1');
        expect(request.request.params.get('PageSize')).toBe('20');
        expect(request.request.params.has('Search')).toBe(false);
        request.flush(page([clinic(9, 'عيادة النيل', 'Nile Clinic'), clinic(10, 'عيادة النور', 'Al Noor Clinic')]));
        await settle();

        // A clinic the user already has is not offered again.
        const offered = [...picker().querySelectorAll('ul button')].map((b) => b.textContent?.replace(/\s+/g, ' ').trim());
        expect(offered).toHaveLength(1);
        expect(offered[0]).toContain('عيادة النور');
        expect(document.activeElement).not.toBeNull();
      });

      it('says when there are more clinics than shown, and asks to refine the search', async () => {
        await open(detail(5, 'ann'));

        const request = await openPicker();
        request.flush(page([clinic(10, 'عيادة النور', 'Al Noor Clinic')], 57));
        await settle();

        expect(picker().textContent).toContain(uar.clinics.more.replace('{{ shown }}', '1').replace('{{ total }}', '57'));
      });

      it('searching asks again with the typed text (debounced) and shows the new matches', async () => {
        await open(detail(5, 'ann'));
        (await openPicker()).flush(page([clinic(10, 'عيادة النور', 'Al Noor Clinic')]));
        await settle();

        search().value = 'sun';
        search().dispatchEvent(new InputEvent('input'));
        await new Promise((resolve) => setTimeout(resolve, 400));
        const request = clinicRequest();
        expect(request.request.params.get('Search')).toBe('sun');
        request.flush(page([clinic(11, 'عيادة الشمس', 'Sun Clinic')]));
        await settle();

        expect(picker().textContent).toContain('Sun Clinic');
        expect(picker().textContent).not.toContain('Al Noor');
      });

      it('says so when nothing matches, and when the search fails (with Retry)', async () => {
        await open(detail(5, 'ann'));
        (await openPicker()).flush(page([]));
        await settle();
        expect(picker().textContent).toContain(uar.clinics.no_matches);

        search().value = 'zzz';
        search().dispatchEvent(new InputEvent('input', { isComposing: true })); // typed, not yet debounced
        one<HTMLFormElement>('[role=search]')!.dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();
        flushProblem(clinicRequest(), 500, 'error.unexpected');
        await settle();
        expect(picker().querySelector('[role=alert]')!.textContent).toContain(uar.clinics.search_failed);

        [...picker().querySelectorAll<HTMLButtonElement>('[role=alert] button')][0].click();
        await settle();
        clinicRequest().flush(page([]));
      });

      it('picking a clinic adds an unsaved card with nothing selected; Save needs a selection', async () => {
        await open(detail(5, 'ann'));
        (await openPicker()).flush(page([clinic(10, 'عيادة النور', 'Al Noor Clinic')]));
        await settle();

        picker().querySelector<HTMLButtonElement>('ul button')!.click();
        await settle();

        expect(cards()).toHaveLength(1);
        expect(cards()[0].textContent).toContain(uar.clinics.unsaved);
        expect(boxesOf(cards()[0])[0].checked).toBe(false);
        expect(cardButton(cards()[0], uar.clinics.save).disabled).toBe(true);
        expect(editor().querySelector('[role=search]')).toBeNull(); // the picker closed

        boxesOf(cards()[0])[0].click();
        await settle();
        expect(cardButton(cards()[0], uar.clinics.save).disabled).toBe(false);
      });

      it('saving a new clinic sends the full set for it, then shows the server state', async () => {
        await open(detail(5, 'ann'));
        (await openPicker()).flush(page([clinic(10, 'عيادة النور', 'Al Noor Clinic')]));
        await settle();
        picker().querySelector<HTMLButtonElement>('ul button')!.click();
        await settle();
        boxesOf(cards()[0])[0].click();
        await settle();

        cardButton(cards()[0], uar.clinics.save).click();
        await settle();
        const put = http.expectOne(`${USER_URL}/clinics/10/permissions`);
        expect(put.request.body).toEqual({ permissions: ['doctors.manage'] });
        put.flush(detail(5, 'ann'));
        await settle();
        http.expectOne(USER_URL).flush(
          detail(5, 'ann', { clinicPermissions: [clinicGrant(10, 'عيادة النور', 'Al Noor Clinic', ['doctors.manage'])] }),
        );
        await settle();

        expect(cards()).toHaveLength(1);
        expect(cards()[0].textContent).not.toContain(uar.clinics.unsaved);
        expect(cardButton(cards()[0], uar.clinics.remove_all)).toBeDefined();
      });

      it('Cancel on an unsaved card removes it without a request; Cancel in the picker closes it', async () => {
        await open(detail(5, 'ann'));
        (await openPicker()).flush(page([clinic(10, 'عيادة النور', 'Al Noor Clinic')]));
        await settle();
        picker().querySelector<HTMLButtonElement>('ul button')!.click();
        await settle();

        cardButton(cards()[0], uar.clinics.cancel_add).click();
        await settle();
        expect(cards()).toHaveLength(0);

        (await openPicker()).flush(page([]));
        await settle();
        [...picker().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === uar.clinics.cancel_add)!.click();
        await settle();
        expect(editor().querySelector('[role=search]')).toBeNull();
        http.expectNone(`${USER_URL}/clinics/10/permissions`);
      });
    });
  });

  describe('saving your own permissions keeps the session honest', () => {
    const ownAdmin = detail(5, 'ann', {
      globalPermissions: ['users.manage', 'clinics.manage'],
      clinicPermissions: [clinicGrant(9, 'عيادة النيل', 'Nile Clinic', ['doctors.manage'])],
    });
    const globalBoxes = () => [...one('cb-global-permissions-editor')!.querySelectorAll<HTMLInputElement>('input[type=checkbox]')];

    async function saveGlobal(change: () => void, reloaded: UserDetail): Promise<void> {
      change();
      await settle();
      [...one('cb-global-permissions-editor')!.querySelectorAll<HTMLButtonElement>('button')]
        .find((b) => b.textContent?.trim() === uar.global.save)!
        .click();
      await settle();
      http.expectOne(`${USER_URL}/global-permissions`).flush(reloaded);
      await settle();
      http.expectOne(USER_URL).flush(reloaded);
      await settle();
    }

    it('refreshes the session after saving your own global permissions and stays when users.manage is kept', async () => {
      await open(ownAdmin, { meId: 5, me: { permissions: ['users.manage', 'clinics.manage'] } });

      const reloaded = { ...ownAdmin, globalPermissions: ['users.manage'] };
      await saveGlobal(() => globalBoxes()[2].click(), reloaded);
      http.expectOne(ME).flush(meBody({ id: 5, permissions: ['users.manage'] }));
      await settle();

      expect(session.can('clinics.manage')).toBe(false);
      expect(session.can('users.manage')).toBe(true);
      expect(router.url).toBe('/users/5');
    });

    it('goes to the home page when the saved change removed your own users.manage', async () => {
      await open(ownAdmin, { meId: 5, me: { permissions: ['users.manage', 'clinics.manage'] } });

      await saveGlobal(() => globalBoxes()[0].click(), { ...ownAdmin, globalPermissions: ['clinics.manage'] });
      http.expectOne(ME).flush(meBody({ id: 5, permissions: ['clinics.manage'] }));
      await settle();

      expect(session.can('users.manage')).toBe(false);
      expect(router.url).toBe('/');
    });

    it('refreshes the session after saving your own clinic permissions too', async () => {
      await open(ownAdmin, { meId: 5, me: { permissions: ['users.manage'], clinicPermissions: ownAdmin.clinicPermissions } });
      expect(session.canIn('doctors.manage', 9)).toBe(true);

      const card = one('cb-clinic-permissions-editor')!.querySelector<HTMLElement>(':scope > section > ul > li')!;
      [...card.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === uar.clinics.remove_all)!.click();
      await settle();
      http.expectOne(`${USER_URL}/clinics/9/permissions`).flush({ ...ownAdmin, clinicPermissions: [] });
      await settle();
      http.expectOne(USER_URL).flush({ ...ownAdmin, clinicPermissions: [] });
      await settle();
      http.expectOne(ME).flush(meBody({ id: 5, permissions: ['users.manage'], clinicPermissions: [] }));
      await settle();

      expect(session.canIn('doctors.manage', 9)).toBe(false);
      expect(router.url).toBe('/users/5');
    });

    it('stays put, with the old session, when the refresh itself fails', async () => {
      await open(ownAdmin, { meId: 5, me: { permissions: ['users.manage', 'clinics.manage'] } });

      await saveGlobal(() => globalBoxes()[2].click(), { ...ownAdmin, globalPermissions: ['users.manage'] });
      flushProblem(http.expectOne(ME), 500, 'error.unexpected');
      await settle();

      expect(session.can('users.manage')).toBe(true);
      expect(router.url).toBe('/users/5');
    });

    it("does not touch the session when somebody else's permissions are saved", async () => {
      await open(ANN, { meId: 1, me: { permissions: ['users.manage'] } });

      const boxes = [...one('cb-global-permissions-editor')!.querySelectorAll<HTMLInputElement>('input[type=checkbox]')];
      boxes[0].click();
      await settle();
      [...one('cb-global-permissions-editor')!.querySelectorAll<HTMLButtonElement>('button')]
        .find((b) => b.textContent?.trim() === uar.global.save)!
        .click();
      await settle();
      http.expectOne(`${USER_URL}/global-permissions`).flush(ANN);
      await settle();
      http.expectOne(USER_URL).flush(ANN);
      await settle();

      http.expectNone(ME);
    });
  });
});
