import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { CLINICS_URL, clinic, NILE } from '../../../../testing/clinics-testing';
import {
  arabicTranslations as ar,
  clinicsArabic as car,
} from '../../../../testing/transloco-testing';
import { LanguageService } from '../../../core/i18n/language.service';
import { ClinicsSession } from '../clinics-session';
import { NAME_MAX_LENGTH, ClinicForm } from './clinic-form';

const ROUTES: Routes = [
  { path: 'clinics/new', component: ClinicForm },
  { path: 'clinics/:id/edit', component: ClinicForm },
  { path: '**', component: Stub },
];

describe('ClinicForm', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim();
  const input = (id: string) => one<HTMLInputElement>(`#${id}`)!;
  const errorsOf = (field: 'ar' | 'en') => text(`#clinic-name-${field}-error`) ?? '';

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
    await signIn(['clinics.manage']);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load('clinics/ar');
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, ClinicForm);
    await settle();
  }

  async function fill(nameAr: string, nameEn: string): Promise<void> {
    for (const [id, value] of [['clinic-name-ar', nameAr], ['clinic-name-en', nameEn]] as const) {
      input(id).value = value;
      input(id).dispatchEvent(new Event('input'));
    }
    await settle();
  }

  async function submit(): Promise<void> {
    one<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  /** Opens the edit page for clinic 1 and loads it. */
  async function openEdit(record = NILE): Promise<void> {
    const opening = open('/clinics/1/edit');
    await Promise.resolve();
    // The load is issued once the page exists; flush it when it appears.
    for (let attempt = 0; attempt < 20; attempt++) {
      const [pending] = http.match(`${CLINICS_URL}/1`);
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
      providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('clinics')],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  describe('create', () => {
    beforeEach(() => open('/clinics/new'));

    it('shows labelled fields, each with its own language and direction, and no request', () => {
      expect(text('h2')).toBe(car.form.title_new);
      expect(one('label[for=clinic-name-ar]')?.textContent?.trim()).toBe(car.form.name_ar);
      expect(one('label[for=clinic-name-en]')?.textContent?.trim()).toBe(car.form.name_en);
      expect(input('clinic-name-ar').getAttribute('lang')).toBe('ar');
      expect(input('clinic-name-ar').getAttribute('dir')).toBe('rtl');
      expect(input('clinic-name-en').getAttribute('lang')).toBe('en');
      expect(input('clinic-name-en').getAttribute('dir')).toBe('ltr');
      expect(document.activeElement).toBe(one('h2'));
    });

    it('requires both names (also whitespace) with the back-end keys, focusing the first invalid field', async () => {
      document.body.appendChild(root());
      await fill('   ', '');

      await submit();

      expect(errorsOf('ar')).toBe(ar.error.clinic.name_ar_required);
      expect(errorsOf('en')).toBe(ar.error.clinic.name_en_required);
      expect(input('clinic-name-ar').getAttribute('aria-invalid')).toBe('true');
      expect(document.activeElement).toBe(input('clinic-name-ar'));
      http.expectNone(CLINICS_URL);
      root().remove();
    });

    it('rejects a name longer than 100 characters, accepts exactly 100', async () => {
      await fill('ا'.repeat(NAME_MAX_LENGTH + 1), 'a'.repeat(NAME_MAX_LENGTH));

      await submit();

      expect(errorsOf('ar')).toBe(ar.error.clinic.name_ar_too_long);
      expect(errorsOf('en')).toBe('');
      http.expectNone(CLINICS_URL);
    });

    it('creates with trimmed names, remembers a message, and returns to the list query', async () => {
      TestBed.inject(ClinicsSession).lastQuery.set({ q: 'x', page: 2 });
      await fill('  القلب  ', '  Cardiology ');

      const done = submit();
      await settle();
      const request = http.expectOne(CLINICS_URL);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ nameAr: 'القلب', nameEn: 'Cardiology', address: null, phone: null });
      request.flush(NILE, { status: 201, statusText: 'Created' });
      await done;
      await settle();

      expect(router.url).toBe('/clinics?q=x&page=2');
      expect(TestBed.inject(ClinicsSession).takeFlash()).toBe('clinics.flash.saved_created');
    });

    it('disables Save and marks it busy while saving', async () => {
      await fill('القلب', 'Cardiology');

      const done = submit();
      await settle();
      const save = one<HTMLButtonElement>('button[type=submit]')!;
      expect(save.disabled).toBe(true);
      expect(save.getAttribute('aria-busy')).toBe('true');
      expect(save.textContent?.trim()).toBe(car.form.saving);

      http.expectOne(CLINICS_URL).flush(NILE, { status: 201, statusText: 'Created' });
      await done;
    });

    it('Cancel goes back to the remembered list query', async () => {
      TestBed.inject(ClinicsSession).lastQuery.set({ q: 'x' });
      harness.detectChanges();

      const cancel = [...root().querySelectorAll('a')].find((a) => a.textContent?.trim() === car.form.cancel)!;
      expect(cancel.getAttribute('href')).toBe('/clinics?q=x');
    });
  });

  describe('server errors', () => {
    beforeEach(async () => {
      await open('/clinics/new');
      await fill('القلب', 'Cardiology');
    });

    async function failWith(status: number, key: string, body: Record<string, unknown> = {}): Promise<void> {
      const done = submit();
      await settle();
      flushProblem(http.expectOne(CLINICS_URL), status, key, { body });
      await done;
      await settle();
    }

    it('maps a 400 onto the fields it names, as translated messages', async () => {
      await failWith(400, 'error.validation.failed', {
        errors: {
          nameAr: ['error.clinic.name_ar_invalid'],
          nameEn: ['error.clinic.name_en_too_long'],
        },
      });

      expect(errorsOf('ar')).toBe(ar.error.clinic.name_ar_invalid);
      expect(errorsOf('en')).toBe(ar.error.clinic.name_en_too_long);
      expect(one('form')!.previousElementSibling?.textContent?.trim() ?? '').not.toContain(ar.error.unexpected);
    });

    it('shows name_ar_taken under the Arabic field only', async () => {
      await failWith(409, 'error.clinic.name_ar_taken');

      expect(errorsOf('ar')).toBe(ar.error.clinic.name_ar_taken);
      expect(errorsOf('en')).toBe('');
      expect(router.url).toBe('/clinics/new');
      expect(input('clinic-name-ar').value).toBe('القلب'); // nothing the user typed is lost
    });

    it('shows name_en_taken under the English field only', async () => {
      await failWith(409, 'error.clinic.name_en_taken');

      expect(errorsOf('en')).toBe(ar.error.clinic.name_en_taken);
      expect(errorsOf('ar')).toBe('');
    });

    it('keeps the user on the page and lets them fix the name and save again', async () => {
      await failWith(409, 'error.clinic.name_ar_taken');

      await fill('قلب وأوعية', 'Cardiology');
      const done = submit();
      await settle();
      const request = http.expectOne(CLINICS_URL);
      expect(request.request.body.nameAr).toBe('قلب وأوعية');
      request.flush(NILE, { status: 201, statusText: 'Created' });
      await done;
      await settle();

      expect(router.url).toBe('/clinics');
    });

    it('clears a server error on a field as soon as the user edits that field', async () => {
      await failWith(409, 'error.clinic.name_ar_taken');
      expect(errorsOf('ar')).toBe(ar.error.clinic.name_ar_taken);

      input('clinic-name-ar').value = 'قلب 2';
      input('clinic-name-ar').dispatchEvent(new Event('input'));
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
      http.expectOne(CLINICS_URL).error(new ProgressEvent('error'));
      await done;
      await settle();

      expect(text('[role=alert]')).toContain(ar.error.network);
    });
  });

  describe('edit', () => {
    it('loads the clinic, shows its details, and sends the rowVersion back on save', async () => {
      await openEdit(clinic(1, 'القلب', 'Cardiology', 'AAAAAAAZ'));

      expect(text('h2')).toBe(car.form.title_edit);
      expect(input('clinic-name-ar').value).toBe('القلب');
      expect(input('clinic-name-en').value).toBe('Cardiology');
      expect(text('section > p')).toContain(car.form.never_updated);

      await fill('القلب والأوعية', 'Cardiology');
      const done = submit();
      await settle();
      const request = http.expectOne(`${CLINICS_URL}/1`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ nameAr: 'القلب والأوعية', nameEn: 'Cardiology', address: null, phone: null, rowVersion: 'AAAAAAAZ' });
      request.flush(NILE);
      await done;
      await settle();

      expect(router.url).toBe('/clinics');
      expect(TestBed.inject(ClinicsSession).takeFlash()).toBe('clinics.flash.saved_updated');
    });

    it('shows "not found" for an id that is not a number, without calling the API', async () => {
      await open('/clinics/abc/edit');

      expect(text('[role=alert]')).toContain(car.not_found.title);
      http.expectNone(`${CLINICS_URL}/abc`);
    });

    it('shows "not found" with a way back when the clinic does not exist', async () => {
      const opening = open('/clinics/1/edit');
      await new Promise((resolve) => setTimeout(resolve, 20));
      flushProblem(http.expectOne(`${CLINICS_URL}/1`), 404, 'error.clinic.not_found');
      await opening;
      await settle();

      expect(text('[role=alert]')).toContain(car.not_found.message);
      expect(one('[role=alert] a')?.getAttribute('href')).toBe('/clinics');
      expect(one('form')).toBeNull();
    });

    it('shows a translated load error and Try again re-issues the request', async () => {
      const opening = open('/clinics/1/edit');
      await new Promise((resolve) => setTimeout(resolve, 20));
      flushProblem(http.expectOne(`${CLINICS_URL}/1`), 500, 'oops');
      await opening;
      await settle();
      expect(text('[role=alert]')).toContain(ar.error.unexpected);

      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === car.retry)!.click();
      await settle();
      http.expectOne(`${CLINICS_URL}/1`).flush(NILE);
      await settle();

      expect(input('clinic-name-en').value).toBe('Nile Clinic');
    });

    it('a 404 while saving (deleted meanwhile) shows "not found"', async () => {
      await openEdit();
      const done = submit();
      await settle();

      flushProblem(http.expectOne(`${CLINICS_URL}/1`), 404, 'error.clinic.not_found');
      await done;
      await settle();

      expect(text('[role=alert]')).toContain(car.not_found.title);
    });
  });

  describe('someone else changed it (409 error.concurrency.conflict)', () => {
    async function conflict(): Promise<void> {
      await openEdit(clinic(1, 'القلب', 'Cardiology', 'AAAAAAAA'));
      await fill('نسختي', 'Mine');
      const done = submit();
      await settle();
      flushProblem(http.expectOne(`${CLINICS_URL}/1`), 409, 'error.concurrency.conflict');
      await done;
      await settle();
    }

    it('shows the banner, keeps what the user typed and disables Save until they reload', async () => {
      await conflict();

      expect(text('[role=alert]')).toContain(car.conflict.title);
      expect(input('clinic-name-ar').value).toBe('نسختي');
      expect(one<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true);
      expect(router.url).toBe('/clinics/1/edit');
      http.expectNone(`${CLINICS_URL}/1`); // nothing was retried or overwritten
    });

    it('Reload fetches the latest, shows it, and lists the earlier entries so they can be re-applied', async () => {
      await conflict();

      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === car.conflict.reload)!.click();
      await settle();
      http.expectOne(`${CLINICS_URL}/1`).flush(clinic(1, 'القلب (محدّث)', 'Cardiology (updated)', 'AAAAAAAB'));
      await settle();

      expect(input('clinic-name-ar').value).toBe('القلب (محدّث)');
      expect(input('clinic-name-en').value).toBe('Cardiology (updated)');
      expect(text('section')).toContain(car.conflict.earlier_title);
      expect(text('section')).toContain('نسختي');
      expect(text('section')).toContain('Mine');
      expect(one<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
      expect(one('[role=alert] .bg-amber-50')).toBeNull();
    });

    it('after Reload, Save sends the new rowVersion', async () => {
      await conflict();
      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === car.conflict.reload)!.click();
      await settle();
      http.expectOne(`${CLINICS_URL}/1`).flush(clinic(1, 'القلب', 'Cardiology', 'AAAAAAAB'));
      await settle();

      const done = submit();
      await settle();
      const request = http.expectOne(`${CLINICS_URL}/1`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body.rowVersion).toBe('AAAAAAAB');
      request.flush(NILE);
      await done;
    });

    it('the earlier-entries panel can be hidden', async () => {
      await conflict();
      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === car.conflict.reload)!.click();
      await settle();
      http.expectOne(`${CLINICS_URL}/1`).flush(NILE);
      await settle();

      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === car.conflict.dismiss)!.click();
      await settle();

      expect(text('section')).not.toContain(car.conflict.earlier_title);
    });

    it('if the clinic was deleted meanwhile, Reload says so instead of failing silently', async () => {
      await conflict();

      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === car.conflict.reload)!.click();
      await settle();
      flushProblem(http.expectOne(`${CLINICS_URL}/1`), 404, 'error.clinic.not_found');
      await settle();

      expect(text('[role=alert]')).toContain(car.not_found.title);
    });

    it('moves focus to the banner so it is announced', async () => {
      document.body.appendChild(root());
      await conflict();

      expect(document.activeElement).toBe(one('[role=alert][tabindex="-1"]'));
      root().remove();
    });
  });
  // ---- the fields Specialties do not have: address and phone (D56) --------------------------------

  describe('address and phone fields', () => {
    const field = (id: string) => one<HTMLInputElement | HTMLTextAreaElement>(`#${id}`)!;

    async function type(id: string, value: string): Promise<void> {
      field(id).value = value;
      field(id).dispatchEvent(new Event('input'));
      await settle();
    }

    /** Submits a valid create form and returns the request body the API received. */
    async function createBody(): Promise<Record<string, unknown>> {
      const done = submit();
      await settle();
      const request = http.expectOne(CLINICS_URL);
      const body = request.request.body as Record<string, unknown>;
      request.flush(NILE, { status: 201, statusText: 'Created' });
      await done;
      await settle();
      return body;
    }

    describe('on a new clinic', () => {
      beforeEach(() => open('/clinics/new'));

      it('labels the address and the phone, with the right input behaviour', () => {
        expect(one('label[for=clinic-address]')?.textContent?.trim()).toBe(car.form.address);
        expect(one('label[for=clinic-phone]')?.textContent?.trim()).toBe(car.form.phone);
        expect(field('clinic-address').tagName).toBe('TEXTAREA');
        expect(field('clinic-address').getAttribute('dir')).toBe('auto');
        const phone = field('clinic-phone');
        expect(phone.getAttribute('type')).toBe('tel');
        expect(phone.getAttribute('inputmode')).toBe('tel');
        expect(phone.getAttribute('dir')).toBe('ltr');
        expect(phone.getAttribute('aria-describedby')).toContain('clinic-phone-hint');
      });

      it('shows a hint under the phone with the accepted formats, each example left to right', () => {
        const hint = one('#clinic-phone-hint')!;

        expect(hint.querySelector('p')?.textContent?.trim()).toBe(car.form.phone_hint);
        const examples = [...hint.querySelectorAll('li bdi')];
        expect(examples.map((e) => e.textContent?.trim())).toEqual([
          car.form.phone_example_mobile,
          car.form.phone_example_landline,
          car.form.phone_example_international,
        ]);
        expect(examples.every((e) => e.getAttribute('dir') === 'ltr')).toBe(true);
        expect([...hint.querySelectorAll('li')].map((li) => li.textContent)).toEqual([
          expect.stringContaining(car.form.phone_kind_mobile),
          expect.stringContaining(car.form.phone_kind_landline),
          expect.stringContaining(car.form.phone_kind_international),
        ]);
      });

      it('shows a visible character counter for the address that counts the trimmed text', async () => {
        const counter = () => text('#clinic-address-counter');
        expect(counter()).toBe(car.form.address_counter.replace('{{ count }}', '0').replace('{{ max }}', '300'));

        await type('clinic-address', '  شارع النيل  ');

        expect(counter()).toBe(car.form.address_counter.replace('{{ count }}', '10').replace('{{ max }}', '300'));
        expect(one('#clinic-address-counter')?.getAttribute('aria-live')).toBeNull(); // not chatty
        expect(field('clinic-address').getAttribute('aria-describedby')).toContain('clinic-address-counter');
      });

      it('rejects an address over 300 characters (after trimming) with the back-end key, and accepts exactly 300', async () => {
        await fill('عيادة', 'Clinic');
        await type('clinic-address', 'ش'.repeat(301));

        await submit();

        expect(text('#clinic-address-error')).toBe(ar.error.clinic.address_too_long);
        expect(field('clinic-address').getAttribute('aria-invalid')).toBe('true');
        http.expectNone(CLINICS_URL);

        await type('clinic-address', `   ${'ش'.repeat(300)}   `); // padding does not count
        expect((await createBody())['address']).toBe('ش'.repeat(300));
      });

      it('only guards the phone length on the client: more than 32 characters is rejected, nothing else', async () => {
        await fill('عيادة', 'Clinic');
        await type('clinic-phone', '0'.repeat(33));

        await submit();

        expect(text('#clinic-phone-error')).toBe(ar.error.clinic.phone_invalid);
        http.expectNone(CLINICS_URL);
      });

      it('leaves every other phone rule to the server: a number the API will refuse is still sent', async () => {
        await fill('عيادة', 'Clinic');
        await type('clinic-phone', 'abc');

        expect((await createBody())['phone']).toBe('abc');
      });

      it('sends what was typed, Arabic-Indic digits included, and a trimmed address', async () => {
        await fill('عيادة النيل', 'Nile Clinic');
        await type('clinic-address', '  ١٢ شارع النيل، الجيزة  ');
        await type('clinic-phone', '٠١٠ ١٢٣٤ ٥٦٧٨');

        const body = await createBody();

        expect(body['phone']).toBe('٠١٠ ١٢٣٤ ٥٦٧٨'); // exactly as typed: the server normalises it
        expect(body['address']).toBe('١٢ شارع النيل، الجيزة');
      });

      it('sends null for a blank address and a blank phone', async () => {
        await fill('عيادة', 'Clinic');
        await type('clinic-address', '   ');
        await type('clinic-phone', '  ');

        const body = await createBody();

        expect(body['address']).toBeNull();
        expect(body['phone']).toBeNull();
      });

      it('sends null for an address and a phone that were never touched', async () => {
        await fill('عيادة', 'Clinic');

        const body = await createBody();

        expect(body).toEqual({ nameAr: 'عيادة', nameEn: 'Clinic', address: null, phone: null });
      });
    });

    describe('server errors on the new fields', () => {
      beforeEach(async () => {
        await open('/clinics/new');
        await fill('عيادة', 'Clinic');
        await type('clinic-address', 'Street');
        await type('clinic-phone', '12345');
      });

      async function failWith(status: number, key: string, body: Record<string, unknown> = {}): Promise<void> {
        const done = submit();
        await settle();
        flushProblem(http.expectOne(CLINICS_URL), status, key, { body });
        await done;
        await settle();
      }

      it('shows error.clinic.phone_invalid under the phone, and only there', async () => {
        await failWith(400, 'error.validation.failed', { errors: { phone: ['error.clinic.phone_invalid'] } });

        expect(text('#clinic-phone-error')).toBe(ar.error.clinic.phone_invalid);
        expect(text('#clinic-address-error') ?? '').toBe('');
        expect(errorsOf('ar')).toBe('');
        expect(field('clinic-phone').getAttribute('aria-invalid')).toBe('true');
      });

      it('shows error.clinic.address_too_long under the address, and only there', async () => {
        await failWith(400, 'error.validation.failed', { errors: { address: ['error.clinic.address_too_long'] } });

        expect(text('#clinic-address-error')).toBe(ar.error.clinic.address_too_long);
        expect(text('#clinic-phone-error') ?? '').toBe('');
      });

      it('shows both at once, each on its own field', async () => {
        await failWith(400, 'error.validation.failed', {
          errors: { address: ['error.clinic.address_too_long'], phone: ['error.clinic.phone_invalid'] },
        });

        expect(text('#clinic-address-error')).toBe(ar.error.clinic.address_too_long);
        expect(text('#clinic-phone-error')).toBe(ar.error.clinic.phone_invalid);
      });

      it('places a phone_invalid that arrives as the problem key (a value that skipped the validator) on the phone', async () => {
        await failWith(400, 'error.clinic.phone_invalid');

        expect(text('#clinic-phone-error')).toBe(ar.error.clinic.phone_invalid);
        expect(one('form')!.previousElementSibling?.textContent ?? '').not.toContain(ar.error.unexpected);
      });

      it('clears the phone error as soon as the user edits the phone', async () => {
        await failWith(400, 'error.validation.failed', { errors: { phone: ['error.clinic.phone_invalid'] } });

        await type('clinic-phone', '01012345678');

        expect(text('#clinic-phone-error') ?? '').toBe('');
      });

      it('keeps what the user typed in every field after a failure', async () => {
        await failWith(400, 'error.validation.failed', { errors: { phone: ['error.clinic.phone_invalid'] } });

        expect(field('clinic-address').value).toBe('Street');
        expect(field('clinic-phone').value).toBe('12345');
      });

      it('maps the name-taken conflicts of a clinic onto the right name field', async () => {
        await failWith(409, 'error.clinic.name_ar_taken');
        expect(errorsOf('ar')).toBe(ar.error.clinic.name_ar_taken);
        expect(errorsOf('en')).toBe('');

        await fill('عيادة 2', 'Clinic 2');
        await failWith(409, 'error.clinic.name_en_taken');
        expect(errorsOf('en')).toBe(ar.error.clinic.name_en_taken);
        expect(errorsOf('ar')).toBe('');
      });
    });

    describe('editing: the untouched-phone guard', () => {
      const putBody = async (): Promise<Record<string, unknown>> => {
        const done = submit();
        await settle();
        const request = http.expectOne(`${CLINICS_URL}/1`);
        const body = request.request.body as Record<string, unknown>;
        request.flush(NILE);
        await done;
        await settle();
        return body;
      };

      it('shows the stored E.164 phone in its local form, and the address as stored', async () => {
        await openEdit(clinic(1, 'عيادة', 'Clinic', { address: '12 شارع النيل', phone: '+201012345678' }));

        expect(field('clinic-phone').value).toBe('010 1234 5678');
        expect(field('clinic-address').value).toBe('12 شارع النيل');
        expect(text('#clinic-address-counter')).toBe(
          car.form.address_counter.replace('{{ count }}', '13').replace('{{ max }}', '300'),
        );
      });

      it.each([
        ['an Egyptian mobile', '+201012345678'],
        ['a Cairo landline', '+20223456789'],
        ['an Alexandria landline', '+2031234567'],
        ['a 3-digit-area landline', '+20403123456'],
        ['a foreign number', '+14155552671'],
        ['an Egyptian number of an unrecognised shape', '+2012345678'],
      ])('an untouched phone (%s) is sent back as the stored E.164, exactly', async (_name, stored) => {
        await openEdit(clinic(1, 'عيادة', 'Clinic', { address: 'Street', phone: stored }));

        const body = await putBody();

        expect(body['phone']).toBe(stored);
      });

      it('an untouched address is sent back as stored, and an absent phone and address stay null', async () => {
        await openEdit(clinic(1, 'عيادة', 'Clinic'));

        const body = await putBody();

        expect(body['phone']).toBeNull();
        expect(body['address']).toBeNull();
      });

      it('a phone retyped with the same digits in another form (Arabic-Indic) is sent exactly as typed', async () => {
        await openEdit(clinic(1, 'عيادة', 'Clinic', { phone: '+201012345678' }));

        await type('clinic-phone', '٠١٠١٢٣٤٥٦٧٨');
        const body = await putBody();

        expect(body['phone']).toBe('٠١٠١٢٣٤٥٦٧٨');
        expect(body['phone']).not.toBe('+201012345678');
      });

      it('a phone retyped in the stored E.164 form is sent as typed too', async () => {
        await openEdit(clinic(1, 'عيادة', 'Clinic', { phone: '+201012345678' }));

        await type('clinic-phone', '+20 10 1234 5678');
        const body = await putBody();

        expect(body['phone']).toBe('+20 10 1234 5678');
      });

      it('a changed phone is sent as typed', async () => {
        await openEdit(clinic(1, 'عيادة', 'Clinic', { phone: '+201012345678' }));

        await type('clinic-phone', '02 2345 6789');
        const body = await putBody();

        expect(body['phone']).toBe('02 2345 6789');
      });

      it('editing something else leaves the phone alone', async () => {
        await openEdit(clinic(1, 'عيادة', 'Clinic', { address: 'Old', phone: '+20223456789' }));

        await type('clinic-address', 'New address');
        const body = await putBody();

        expect(body['phone']).toBe('+20223456789');
        expect(body['address']).toBe('New address');
      });

      it('clearing the phone and the address sends null for both (a full replace)', async () => {
        await openEdit(clinic(1, 'عيادة', 'Clinic', { address: 'Street', phone: '+201012345678' }));

        await type('clinic-phone', '');
        await type('clinic-address', '   ');
        const body = await putBody();

        expect(body['phone']).toBeNull();
        expect(body['address']).toBeNull();
      });
    });

    describe('conflict flow with the new fields', () => {
      async function conflict(): Promise<void> {
        await openEdit(clinic(1, 'عيادة', 'Clinic', { address: 'Old', phone: '+201012345678', rowVersion: 'AAAAAAAA' }));
        await type('clinic-address', 'My address');
        await type('clinic-phone', '٠١١٢٣٤٥٦٧٨٩');
        const done = submit();
        await settle();
        flushProblem(http.expectOne(`${CLINICS_URL}/1`), 409, 'error.concurrency.conflict');
        await done;
        await settle();
      }

      it('keeps the typed address and phone, and disables Save until Reload', async () => {
        await conflict();

        expect(field('clinic-address').value).toBe('My address');
        expect(field('clinic-phone').value).toBe('٠١١٢٣٤٥٦٧٨٩');
        expect(one<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true);
      });

      it('after Reload shows the latest values and lists the earlier address and phone, left to right', async () => {
        await conflict();

        [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === car.conflict.reload)!.click();
        await settle();
        http
          .expectOne(`${CLINICS_URL}/1`)
          .flush(clinic(1, 'عيادة', 'Clinic', { address: 'Theirs', phone: '+20223456789', rowVersion: 'AAAAAAAB' }));
        await settle();

        expect(field('clinic-address').value).toBe('Theirs');
        expect(field('clinic-phone').value).toBe('02 2345 6789');
        const panel = [...root().querySelectorAll('div')].find((d) => d.textContent?.includes(car.conflict.earlier_title))!;
        expect(panel.textContent).toContain('My address');
        expect(panel.textContent).toContain('٠١١٢٣٤٥٦٧٨٩');
        expect(panel.querySelector('bdi[dir=ltr].whitespace-nowrap')?.textContent).toBe('٠١١٢٣٤٥٦٧٨٩');
      });

      it('after Reload an untouched phone is the reloaded stored value, not the old one', async () => {
        await conflict();
        [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === car.conflict.reload)!.click();
        await settle();
        http
          .expectOne(`${CLINICS_URL}/1`)
          .flush(clinic(1, 'عيادة', 'Clinic', { address: 'Theirs', phone: '+20223456789', rowVersion: 'AAAAAAAB' }));
        await settle();

        const done = submit();
        await settle();
        const request = http.expectOne(`${CLINICS_URL}/1`);
        expect(request.request.body).toMatchObject({ phone: '+20223456789', address: 'Theirs', rowVersion: 'AAAAAAAB' });
        request.flush(NILE);
        await done;
      });
    });
  });

});
