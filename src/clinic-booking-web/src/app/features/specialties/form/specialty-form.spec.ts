import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { CARDIOLOGY, item, SPECIALTIES_URL } from '../../../../testing/specialties-testing';
import {
  arabicTranslations as ar,
  specialtiesArabic as sar,
} from '../../../../testing/transloco-testing';
import { LanguageService } from '../../../core/i18n/language.service';
import { SpecialtiesSession } from '../specialties-session';
import { NAME_MAX_LENGTH, SpecialtyForm } from './specialty-form';

const ROUTES: Routes = [
  { path: 'specialties/new', component: SpecialtyForm },
  { path: 'specialties/:id/edit', component: SpecialtyForm },
  { path: '**', component: Stub },
];

describe('SpecialtyForm', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim();
  const input = (id: string) => one<HTMLInputElement>(`#${id}`)!;
  const errorsOf = (field: 'ar' | 'en') => text(`#specialty-name-${field}-error`) ?? '';

  /** Not whenStable(): a pending HTTP request keeps the app unstable until the test flushes it. */
  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  async function open(url: string): Promise<void> {
    await signIn(['specialties.manage']);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load('specialties/ar');
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, SpecialtyForm);
    await settle();
  }

  async function fill(nameAr: string, nameEn: string): Promise<void> {
    for (const [id, value] of [['specialty-name-ar', nameAr], ['specialty-name-en', nameEn]] as const) {
      input(id).value = value;
      input(id).dispatchEvent(new Event('input'));
    }
    await settle();
  }

  async function submit(): Promise<void> {
    one<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  /** Opens the edit page for specialty 1 and loads it. */
  async function openEdit(record = CARDIOLOGY): Promise<void> {
    const opening = open('/specialties/1/edit');
    await Promise.resolve();
    // The load is issued once the page exists; flush it when it appears.
    for (let attempt = 0; attempt < 20; attempt++) {
      const [pending] = http.match(`${SPECIALTIES_URL}/1`);
      if (pending) {
        pending.flush(record);
        break;
      }
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
    await opening;
    await settle();
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('specialties')],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  describe('create', () => {
    beforeEach(() => open('/specialties/new'));

    it('shows labelled fields, each with its own language and direction, and no request', () => {
      expect(text('h2')).toBe(sar.form.title_new);
      expect(one('label[for=specialty-name-ar]')?.textContent?.trim()).toBe(sar.form.name_ar);
      expect(one('label[for=specialty-name-en]')?.textContent?.trim()).toBe(sar.form.name_en);
      expect(input('specialty-name-ar').getAttribute('lang')).toBe('ar');
      expect(input('specialty-name-ar').getAttribute('dir')).toBe('rtl');
      expect(input('specialty-name-en').getAttribute('lang')).toBe('en');
      expect(input('specialty-name-en').getAttribute('dir')).toBe('ltr');
      expect(document.activeElement).toBe(one('h2'));
    });

    it('requires both names (also whitespace) with the back-end keys, focusing the first invalid field', async () => {
      document.body.appendChild(root());
      await fill('   ', '');

      await submit();

      expect(errorsOf('ar')).toBe(ar.error.specialty.name_ar_required);
      expect(errorsOf('en')).toBe(ar.error.specialty.name_en_required);
      expect(input('specialty-name-ar').getAttribute('aria-invalid')).toBe('true');
      expect(document.activeElement).toBe(input('specialty-name-ar'));
      http.expectNone(SPECIALTIES_URL);
      root().remove();
    });

    it('rejects a name longer than 100 characters, accepts exactly 100', async () => {
      await fill('ا'.repeat(NAME_MAX_LENGTH + 1), 'a'.repeat(NAME_MAX_LENGTH));

      await submit();

      expect(errorsOf('ar')).toBe(ar.error.specialty.name_ar_too_long);
      expect(errorsOf('en')).toBe('');
      http.expectNone(SPECIALTIES_URL);
    });

    it('creates with trimmed names, remembers a message, and returns to the list query', async () => {
      TestBed.inject(SpecialtiesSession).lastQuery.set({ q: 'x', page: 2 });
      await fill('  القلب  ', '  Cardiology ');

      const done = submit();
      await settle();
      const request = http.expectOne(SPECIALTIES_URL);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ nameAr: 'القلب', nameEn: 'Cardiology' });
      request.flush(CARDIOLOGY, { status: 201, statusText: 'Created' });
      await done;
      await settle();

      expect(router.url).toBe('/specialties?q=x&page=2');
      expect(TestBed.inject(SpecialtiesSession).takeFlash()).toBe('specialties.flash.saved_created');
    });

    it('disables Save and marks it busy while saving', async () => {
      await fill('القلب', 'Cardiology');

      const done = submit();
      await settle();
      const save = one<HTMLButtonElement>('button[type=submit]')!;
      expect(save.disabled).toBe(true);
      expect(save.getAttribute('aria-busy')).toBe('true');
      expect(save.textContent?.trim()).toBe(sar.form.saving);

      http.expectOne(SPECIALTIES_URL).flush(CARDIOLOGY, { status: 201, statusText: 'Created' });
      await done;
    });

    it('Cancel goes back to the remembered list query', async () => {
      TestBed.inject(SpecialtiesSession).lastQuery.set({ q: 'x' });
      harness.detectChanges();

      const cancel = [...root().querySelectorAll('a')].find((a) => a.textContent?.trim() === sar.form.cancel)!;
      expect(cancel.getAttribute('href')).toBe('/specialties?q=x');
    });
  });

  describe('server errors', () => {
    beforeEach(async () => {
      await open('/specialties/new');
      await fill('القلب', 'Cardiology');
    });

    async function failWith(status: number, key: string, body: Record<string, unknown> = {}): Promise<void> {
      const done = submit();
      await settle();
      flushProblem(http.expectOne(SPECIALTIES_URL), status, key, { body });
      await done;
      await settle();
    }

    it('maps a 400 onto the fields it names, as translated messages', async () => {
      await failWith(400, 'error.validation.failed', {
        errors: {
          nameAr: ['error.specialty.name_ar_invalid'],
          nameEn: ['error.specialty.name_en_too_long'],
        },
      });

      expect(errorsOf('ar')).toBe(ar.error.specialty.name_ar_invalid);
      expect(errorsOf('en')).toBe(ar.error.specialty.name_en_too_long);
      expect(one('form')!.previousElementSibling?.textContent?.trim() ?? '').not.toContain(ar.error.unexpected);
    });

    it('shows name_ar_taken under the Arabic field only', async () => {
      await failWith(409, 'error.specialty.name_ar_taken');

      expect(errorsOf('ar')).toBe(ar.error.specialty.name_ar_taken);
      expect(errorsOf('en')).toBe('');
      expect(router.url).toBe('/specialties/new');
      expect(input('specialty-name-ar').value).toBe('القلب'); // nothing the user typed is lost
    });

    it('shows name_en_taken under the English field only', async () => {
      await failWith(409, 'error.specialty.name_en_taken');

      expect(errorsOf('en')).toBe(ar.error.specialty.name_en_taken);
      expect(errorsOf('ar')).toBe('');
    });

    it('keeps the user on the page and lets them fix the name and save again', async () => {
      await failWith(409, 'error.specialty.name_ar_taken');

      await fill('قلب وأوعية', 'Cardiology');
      const done = submit();
      await settle();
      const request = http.expectOne(SPECIALTIES_URL);
      expect(request.request.body.nameAr).toBe('قلب وأوعية');
      request.flush(CARDIOLOGY, { status: 201, statusText: 'Created' });
      await done;
      await settle();

      expect(router.url).toBe('/specialties');
    });

    it('clears a server error on a field as soon as the user edits that field', async () => {
      await failWith(409, 'error.specialty.name_ar_taken');
      expect(errorsOf('ar')).toBe(ar.error.specialty.name_ar_taken);

      input('specialty-name-ar').value = 'قلب 2';
      input('specialty-name-ar').dispatchEvent(new Event('input'));
      await settle();

      expect(errorsOf('ar')).toBe('');
    });

    it('shows a form-level translated message for anything else, with the reference to quote', async () => {
      await failWith(500, 'Internal Server Error');

      expect(text('[role=alert]')).toContain(ar.error.unexpected);
      expect(text('[role=alert]')).not.toContain('Internal Server Error');
      expect(text('[role=alert]')).toContain('corr-123');
      expect(errorsOf('ar')).toBe('');
    });

    it('a form-level message that has its own key shows no reference to quote (D52)', async () => {
      await failWith(403, 'error.auth.forbidden');

      expect(text('[role=alert]')).toContain(ar.error.auth.forbidden);
      expect(text('[role=alert]')).not.toContain('corr-123');
    });

    it('never shows a key nobody translated', async () => {
      await failWith(409, 'error.brand.new_conflict');

      expect(text('[role=alert]')).toContain(ar.error.unexpected);
      expect(root().textContent).not.toContain('error.brand.new_conflict');
    });

    it('shows the network message when the server cannot be reached', async () => {
      const done = submit();
      await settle();
      http.expectOne(SPECIALTIES_URL).error(new ProgressEvent('error'));
      await done;
      await settle();

      expect(text('[role=alert]')).toContain(ar.error.network);
    });
  });

  describe('edit', () => {
    it('loads the specialty, shows its details, and sends the rowVersion back on save', async () => {
      await openEdit(item(1, 'القلب', 'Cardiology', 'AAAAAAAZ'));

      expect(text('h2')).toBe(sar.form.title_edit);
      expect(input('specialty-name-ar').value).toBe('القلب');
      expect(input('specialty-name-en').value).toBe('Cardiology');
      expect(text('section > p')).toContain(sar.form.never_updated);

      await fill('القلب والأوعية', 'Cardiology');
      const done = submit();
      await settle();
      const request = http.expectOne(`${SPECIALTIES_URL}/1`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ nameAr: 'القلب والأوعية', nameEn: 'Cardiology', rowVersion: 'AAAAAAAZ' });
      request.flush(CARDIOLOGY);
      await done;
      await settle();

      expect(router.url).toBe('/specialties');
      expect(TestBed.inject(SpecialtiesSession).takeFlash()).toBe('specialties.flash.saved_updated');
    });

    it('shows "not found" for an id that is not a number, without calling the API', async () => {
      await open('/specialties/abc/edit');

      expect(text('[role=alert]')).toContain(sar.not_found.title);
      http.expectNone(`${SPECIALTIES_URL}/abc`);
    });

    it('shows "not found" with a way back when the specialty does not exist', async () => {
      const opening = open('/specialties/1/edit');
      await new Promise((resolve) => setTimeout(resolve, 20));
      flushProblem(http.expectOne(`${SPECIALTIES_URL}/1`), 404, 'error.specialty.not_found');
      await opening;
      await settle();

      expect(text('[role=alert]')).toContain(sar.not_found.message);
      expect(one('[role=alert] a')?.getAttribute('href')).toBe('/specialties');
      expect(one('form')).toBeNull();
    });

    it('shows a translated load error and Try again re-issues the request', async () => {
      const opening = open('/specialties/1/edit');
      await new Promise((resolve) => setTimeout(resolve, 20));
      flushProblem(http.expectOne(`${SPECIALTIES_URL}/1`), 500, 'oops');
      await opening;
      await settle();
      expect(text('[role=alert]')).toContain(ar.error.unexpected);

      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === sar.retry)!.click();
      await settle();
      http.expectOne(`${SPECIALTIES_URL}/1`).flush(CARDIOLOGY);
      await settle();

      expect(input('specialty-name-en').value).toBe('Cardiology');
    });

    it('a 404 while saving (deleted meanwhile) shows "not found"', async () => {
      await openEdit();
      const done = submit();
      await settle();

      flushProblem(http.expectOne(`${SPECIALTIES_URL}/1`), 404, 'error.specialty.not_found');
      await done;
      await settle();

      expect(text('[role=alert]')).toContain(sar.not_found.title);
    });
  });

  describe('someone else changed it (409 error.concurrency.conflict)', () => {
    async function conflict(): Promise<void> {
      await openEdit(item(1, 'القلب', 'Cardiology', 'AAAAAAAA'));
      await fill('نسختي', 'Mine');
      const done = submit();
      await settle();
      flushProblem(http.expectOne(`${SPECIALTIES_URL}/1`), 409, 'error.concurrency.conflict');
      await done;
      await settle();
    }

    it('shows the banner, keeps what the user typed and disables Save until they reload', async () => {
      await conflict();

      expect(text('[role=alert]')).toContain(sar.conflict.title);
      expect(input('specialty-name-ar').value).toBe('نسختي');
      expect(one<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true);
      expect(router.url).toBe('/specialties/1/edit');
      http.expectNone(`${SPECIALTIES_URL}/1`); // nothing was retried or overwritten
    });

    it('Reload fetches the latest, shows it, and lists the earlier entries so they can be re-applied', async () => {
      await conflict();

      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === sar.conflict.reload)!.click();
      await settle();
      http.expectOne(`${SPECIALTIES_URL}/1`).flush(item(1, 'القلب (محدّث)', 'Cardiology (updated)', 'AAAAAAAB'));
      await settle();

      expect(input('specialty-name-ar').value).toBe('القلب (محدّث)');
      expect(input('specialty-name-en').value).toBe('Cardiology (updated)');
      expect(text('section')).toContain(sar.conflict.earlier_title);
      expect(text('section')).toContain('نسختي');
      expect(text('section')).toContain('Mine');
      expect(one<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
      expect(one('[role=alert] .bg-amber-50')).toBeNull();
    });

    it('after Reload, Save sends the new rowVersion', async () => {
      await conflict();
      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === sar.conflict.reload)!.click();
      await settle();
      http.expectOne(`${SPECIALTIES_URL}/1`).flush(item(1, 'القلب', 'Cardiology', 'AAAAAAAB'));
      await settle();

      const done = submit();
      await settle();
      const request = http.expectOne(`${SPECIALTIES_URL}/1`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body.rowVersion).toBe('AAAAAAAB');
      request.flush(CARDIOLOGY);
      await done;
    });

    it('the earlier-entries panel can be hidden', async () => {
      await conflict();
      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === sar.conflict.reload)!.click();
      await settle();
      http.expectOne(`${SPECIALTIES_URL}/1`).flush(CARDIOLOGY);
      await settle();

      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === sar.conflict.dismiss)!.click();
      await settle();

      expect(text('section')).not.toContain(sar.conflict.earlier_title);
    });

    it('if the specialty was deleted meanwhile, Reload says so instead of failing silently', async () => {
      await conflict();

      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === sar.conflict.reload)!.click();
      await settle();
      flushProblem(http.expectOne(`${SPECIALTIES_URL}/1`), 404, 'error.specialty.not_found');
      await settle();

      expect(text('[role=alert]')).toContain(sar.not_found.title);
    });

    it('moves focus to the banner so it is announced', async () => {
      document.body.appendChild(root());
      await conflict();

      expect(document.activeElement).toBe(one('[role=alert][tabindex="-1"]'));
      root().remove();
    });
  });
});
