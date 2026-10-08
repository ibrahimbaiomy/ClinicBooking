import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { ALL_PATIENT_PERMISSIONS, patient, PATIENTS_URL, SARA } from '../../../../testing/patients-testing';
import { arabicTranslations as ar, patientsArabic as par } from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { PatientsSession } from '../patients-session';
import { PatientForm } from './patient-form';

const ROUTES: Routes = [
  { path: 'patients/new', component: PatientForm },
  { path: 'patients/:id/edit', component: PatientForm },
  { path: '**', component: Stub },
];

const MATCHES = [
  { id: 3, name: 'منى علي', phone: '+201012345678' },
  { id: 4, name: 'Mona Ali', phone: '+201012345678' },
];

describe('PatientForm (D63)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  async function open(url: string, permissions = ALL_PATIENT_PERMISSIONS): Promise<void> {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
    await signIn(permissions);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load('patients/ar');
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, PatientForm);
    await settle();
  }

  async function openEdit(record = SARA): Promise<void> {
    await open('/patients/1/edit');
    http.expectOne(`${PATIENTS_URL}/1`).flush(record);
    await settle();
  }

  async function type(id: string, value: string): Promise<void> {
    const input = one<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function submit(): Promise<void> {
    one<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  async function fill(name = 'سارة', phone = '010 1234 5678'): Promise<void> {
    await type('patient-name', name);
    await type('patient-phone', phone);
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('patients')] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  describe('create', () => {
    it('both fields are required; nothing is sent', async () => {
      await open('/patients/new');

      await submit();

      expect(text('#patient-name-error')).toBe(ar.error.patient.name_required);
      expect(text('#patient-phone-error')).toBe(ar.error.patient.phone_required);
      expect(document.activeElement).toBe(one('#patient-name'));
      http.expectNone({ method: 'POST', url: PATIENTS_URL });
    });

    it('the name follows its content direction and the phone is left to right', async () => {
      await open('/patients/new');

      expect(one('#patient-name')!.getAttribute('dir')).toBe('auto');
      expect(one('#patient-phone')!.getAttribute('dir')).toBe('ltr');
      expect(one('#patient-phone')!.getAttribute('type')).toBe('tel');
    });

    it('sends the trimmed name and the phone as typed, then returns to the list with a message', async () => {
      TestBed.inject(PatientsSession).lastQuery.set({ q: 'x' });
      await open('/patients/new');
      await fill('  سارة  ');

      await submit();
      const request = http.expectOne({ method: 'POST', url: PATIENTS_URL });
      expect(request.request.body).toEqual({ name: 'سارة', phone: '010 1234 5678', confirmDuplicatePhone: null });
      request.flush(SARA);
      await settle();

      expect(router.url).toBe('/patients?q=x');
      expect(TestBed.inject(PatientsSession).takeFlash()).toBe('patients.flash.saved_created');
    });

    it('without patients.read, stays on an empty form with the message', async () => {
      await open('/patients/new', ['patients.create']);
      await fill();

      await submit();
      http.expectOne({ method: 'POST', url: PATIENTS_URL }).flush(SARA);
      await settle();

      expect(router.url).toBe('/patients/new');
      expect(text('[role="status"]')).toBe(par.flash.saved_created);
      expect(one<HTMLInputElement>('#patient-name')!.value).toBe('');
      expect(one('a[href^="/patients"]')).toBeNull(); // no list to cancel to
    });
  });

  describe('the duplicate-phone warning', () => {
    async function warned(body: Record<string, unknown> = { matchCount: 2, matches: MATCHES }): Promise<void> {
      await open('/patients/new');
      await fill();
      await submit();
      flushProblem(http.expectOne({ method: 'POST', url: PATIENTS_URL }), 409, 'error.patient.phone_exists', { body });
      await settle();
    }

    it('shows a focused alert listing the matches, keeps the input, and saves nothing', async () => {
      await warned();

      const panel = one<HTMLElement>('#patient-duplicate')!;
      expect(panel.getAttribute('role')).toBe('alert');
      expect(document.activeElement).toBe(panel);
      expect(text('#patient-duplicate-matches')).toContain('منى علي');
      expect(text('#patient-duplicate-matches')).toContain('Mona Ali');
      expect(text('#patient-duplicate-matches')).toContain('010 1234 5678');
      expect(panel.querySelector('a')).toBeNull(); // a match is not a link
      expect(one<HTMLInputElement>('#patient-name')!.value).toBe('سارة');
      expect(router.url).toBe('/patients/new');
    });

    it('says how many more there are than the five listed', async () => {
      await warned({ matchCount: 8, matches: MATCHES });

      expect(text('#patient-duplicate')).toContain(par.duplicate.more.replace('{{ count }}', '6'));
    });

    it('without patients.read on the server side, shows the count only', async () => {
      await warned({ matchCount: 3 });

      expect(text('#patient-duplicate-count')).toBe(par.duplicate.count_only.replace('{{ count }}', '3'));
      expect(one('#patient-duplicate-matches')).toBeNull();
    });

    it('"Save anyway" resends the same values with confirmDuplicatePhone', async () => {
      await warned();

      one<HTMLButtonElement>('#patient-save-anyway')!.click();
      await settle();
      const request = http.expectOne({ method: 'POST', url: PATIENTS_URL });
      expect(request.request.body).toEqual({ name: 'سارة', phone: '010 1234 5678', confirmDuplicatePhone: true });
      request.flush(SARA);
      await settle();

      expect(router.url).toBe('/patients');
    });

    it('"Cancel" hides the panel, sends nothing, and returns to the phone', async () => {
      await warned();

      one<HTMLButtonElement>('#patient-duplicate-cancel')!.click();
      await settle();

      expect(one('#patient-duplicate')).toBeNull();
      expect(document.activeElement).toBe(one('#patient-phone'));
      http.expectNone({ method: 'POST', url: PATIENTS_URL });
    });

    it('changing the phone hides the panel', async () => {
      await warned();

      await type('patient-phone', '010 9999 0000');

      expect(one('#patient-duplicate')).toBeNull();
    });
  });

  describe('edit', () => {
    it('an untouched phone sends the stored E.164 value; a typed one is sent as typed', async () => {
      await openEdit();
      expect(one<HTMLInputElement>('#patient-phone')!.value).toBe('010 1234 5678');

      await type('patient-name', 'سارة أحمد');
      await submit();
      const untouched = http.expectOne({ method: 'PUT', url: `${PATIENTS_URL}/1` });
      expect(untouched.request.body).toEqual({
        name: 'سارة أحمد',
        phone: '+201012345678',
        rowVersion: SARA.rowVersion,
        confirmDuplicatePhone: null,
      });
      flushProblem(untouched, 500, 'error.unexpected');
      await settle();

      await type('patient-phone', '0101 234 5678');
      await submit();
      expect(http.expectOne({ method: 'PUT', url: `${PATIENTS_URL}/1` }).request.body.phone).toBe('0101 234 5678');
    });

    it('a 409 conflict keeps the input, disables Save until Reload, then shows the earlier entries', async () => {
      await openEdit();
      await type('patient-name', 'Mine');

      await submit();
      flushProblem(http.expectOne({ method: 'PUT', url: `${PATIENTS_URL}/1` }), 409, 'error.concurrency.conflict');
      await settle();
      expect(text('#patient-conflict')).toContain(par.conflict.title);
      expect(one<HTMLButtonElement>('#patient-save')!.disabled).toBe(true);

      one<HTMLButtonElement>('#patient-reload')!.click();
      await settle();
      http.expectOne(`${PATIENTS_URL}/1`).flush(patient(1, 'Theirs', '+201012345678', { rowVersion: 'AAAAAAAAAAk=' }));
      await settle();

      expect(one<HTMLInputElement>('#patient-name')!.value).toBe('Theirs');
      expect(one<HTMLButtonElement>('#patient-save')!.disabled).toBe(false);
      expect(text('#patient-earlier')).toContain('Mine');
    });

    it('field keys go on their fields; a keyed form-level failure has no support reference', async () => {
      await openEdit();

      await submit();
      flushProblem(http.expectOne({ method: 'PUT', url: `${PATIENTS_URL}/1` }), 400, 'error.validation.failed', {
        body: { errors: { phone: ['error.patient.phone_invalid'] } },
      });
      await settle();
      expect(text('#patient-phone-error')).toBe(ar.error.patient.phone_invalid);

      await type('patient-phone', '010 1234 5679');
      await submit();
      flushProblem(http.expectOne({ method: 'PUT', url: `${PATIENTS_URL}/1` }), 403, 'error.auth.forbidden');
      await settle();
      expect(text('#patient-form-error')).toContain(ar.error.auth.forbidden);
      expect(text('#patient-form-error')).not.toContain('corr-123');
    });
  });
});
