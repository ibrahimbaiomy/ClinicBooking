import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { CurrentUser } from '../../../../api/auth-api';
import { Doctor } from '../../../../api/doctors-api';
import { flushProblem, meBody, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import {
  AHMED,
  doctor,
  DOCTORS_URL,
  managerOf,
  referencePage,
  SPECIALTIES,
  SPECIALTIES_URL,
} from '../../../../testing/doctors-testing';
import { arabicTranslations as ar, doctorsArabic as dar } from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { DoctorsSession } from '../doctors-session';
import { DoctorForm } from './doctor-form';

const ROUTES: Routes = [
  { path: 'doctors/new', component: DoctorForm },
  { path: 'doctors/:id/edit', component: DoctorForm },
  { path: '**', component: Stub },
];

describe('DoctorForm (D62)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const all = <T extends Element>(selector: string) => [...root().querySelectorAll<T>(selector)];
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const ids = (prefix: string) => all<HTMLInputElement>(`input[id^="${prefix}"]`).map((i) => i.id.slice(prefix.length));

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  async function start(url: string, user: Partial<CurrentUser>): Promise<void> {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
    await signIn([], 'token-1', user);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load('doctors/ar');
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, DoctorForm);
    await settle();
  }

  /** /doctors/new: answers the session refresh (with `fresh` grants) and the specialty list. */
  async function openCreate(user = managerOf(1), fresh: Partial<CurrentUser> = user): Promise<void> {
    await start('/doctors/new', user);
    http.expectOne('/api/auth/me').flush(meBody(fresh));
    http.expectOne((r) => r.url === SPECIALTIES_URL).flush(referencePage(SPECIALTIES));
    await settle();
  }

  async function openEdit(record: Doctor = AHMED, user = managerOf(1)): Promise<void> {
    await start('/doctors/7/edit', user);
    http.expectOne((r) => r.url === SPECIALTIES_URL).flush(referencePage(SPECIALTIES));
    http.expectOne(`${DOCTORS_URL}/7`).flush(record);
    await settle();
  }

  async function type(id: string, value: string): Promise<void> {
    const input = one<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function check(id: string): Promise<void> {
    const box = one<HTMLInputElement>(`#${id}`)!;
    box.checked = !box.checked;
    box.dispatchEvent(new Event('change'));
    await settle();
  }

  async function submit(): Promise<void> {
    one<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('doctors')] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  describe('create', () => {
    it('refreshes the session, then offers only clinics where the user manages doctors, sorted by Arabic name', async () => {
      // Signed in managing clinic 1 only; the refresh brings a grant made since (clinic 3) and another permission (2).
      await openCreate(managerOf(1), {
        clinicPermissions: [
          ...managerOf(1, 3).clinicPermissions!,
          { clinicId: 2, clinicNameAr: 'عيادة النور', clinicNameEn: 'Al Noor Clinic', permissions: ['other.permission'] },
        ],
      });

      expect(ids('doctor-clinic-')).toEqual(['1', '3']); // النيل before الهرم; clinic 2 has no doctors.manage
      expect(ids('doctor-specialty-')).toEqual(['12', '11', '10']); // أطفال, جلدية, قلب
      expect(all('#doctor-slot option')).toHaveLength(24);
      expect(one<HTMLSelectElement>('#doctor-slot')!.value).toBe('15');
    });

    it('without any managed clinic after the refresh, says so instead of showing the form', async () => {
      await openCreate(managerOf(1), { clinicPermissions: [] });

      expect(text('#doctor-form-no-clinics')).toContain(dar.form.no_clinics);
      expect(one('form')).toBeNull();
    });

    it('an empty form shows every required message and sends nothing', async () => {
      await openCreate();

      await submit();

      expect(text('#doctor-name-ar-error')).toBe(ar.error.doctor.name_ar_required);
      expect(text('#doctor-name-en-error')).toBe(ar.error.doctor.name_en_required);
      expect(text('#doctor-specialties-error')).toBe(ar.error.doctor.specialties_required);
      expect(text('#doctor-clinics-error')).toBe(ar.error.doctor.clinics_required);
      expect(document.activeElement).toBe(one('#doctor-name-ar'));
      http.expectNone({ method: 'POST', url: DOCTORS_URL });
    });

    it('sends trimmed names, the chosen ids and the slot, then opens the new doctor with a message', async () => {
      await openCreate(managerOf(1, 3));
      await type('doctor-name-ar', '  أحمد علي ');
      await type('doctor-name-en', ' Ahmed Ali ');
      await check('doctor-specialty-11');
      await check('doctor-specialty-10');
      await check('doctor-clinic-3');
      const slot = one<HTMLSelectElement>('#doctor-slot')!;
      slot.value = '30';
      slot.dispatchEvent(new Event('input'));
      slot.dispatchEvent(new Event('change'));
      await settle();

      await submit();
      const request = http.expectOne({ method: 'POST', url: DOCTORS_URL });
      expect(request.request.body).toEqual({
        nameAr: 'أحمد علي',
        nameEn: 'Ahmed Ali',
        specialtyIds: [11, 10],
        clinicIds: [3],
        slotMinutes: 30,
      });
      request.flush(doctor(42, 'أحمد علي', 'Ahmed Ali'));
      await settle();

      expect(router.url).toBe('/doctors/42');
      expect(TestBed.inject(DoctorsSession).takeFlash()).toBe('doctors.flash.created');
    });

    it('error.doctor.specialty_unavailable goes on the specialties; a 403 is form-level', async () => {
      await openCreate();
      await type('doctor-name-ar', 'أحمد');
      await type('doctor-name-en', 'Ahmed');
      await check('doctor-specialty-10');
      await check('doctor-clinic-1');

      await submit();
      flushProblem(http.expectOne({ method: 'POST', url: DOCTORS_URL }), 400, 'error.validation.failed', {
        body: { errors: { specialtyIds: ['error.doctor.specialty_unavailable'] } },
      });
      await settle();
      expect(text('#doctor-specialties-error')).toBe(ar.error.doctor.specialty_unavailable);

      await check('doctor-specialty-11'); // a change clears the server error on that field
      await submit();
      flushProblem(http.expectOne({ method: 'POST', url: DOCTORS_URL }), 403, 'error.auth.forbidden');
      await settle();
      expect(text('#doctor-form-error')).toContain(ar.error.auth.forbidden);
      expect(text('#doctor-form-error')).not.toContain('corr-123'); // a keyed failure has no reference (D52)

      await submit();
      flushProblem(http.expectOne({ method: 'POST', url: DOCTORS_URL }), 500, 'error.unexpected');
      await settle();
      expect(text('#doctor-form-error')).toContain(ar.error.unexpected);
      expect(text('#doctor-form-error')).toContain('corr-123');
    });
  });

  describe('edit', () => {
    it('someone who manages none of the doctor\'s clinics is told so, without a form', async () => {
      await openEdit(AHMED, managerOf(3));

      expect(text('#doctor-form-not-allowed')).toContain(dar.form.not_allowed);
      expect(one('form')).toBeNull();
    });

    it('a manager of one of its clinics edits names and specialties only, prefilled', async () => {
      await openEdit(AHMED, managerOf(2));

      expect(one<HTMLInputElement>('#doctor-name-ar')!.value).toBe('أحمد علي');
      expect(one<HTMLInputElement>('#doctor-specialty-10')!.checked).toBe(true);
      expect(one<HTMLInputElement>('#doctor-specialty-12')!.checked).toBe(false);
      expect(one('#doctor-slot')).toBeNull();
      expect(all('input[id^="doctor-clinic-"]')).toHaveLength(0);
    });

    it('offers the doctor\'s own specialties even when they are not in the first 100', async () => {
      await openEdit(doctor(7, 'أ', 'A', { specialties: [{ id: 999, nameAr: 'نادر', nameEn: 'Rare' }] }));

      expect(one<HTMLInputElement>('#doctor-specialty-999')!.checked).toBe(true);
    });

    it('saves a full replace with the row version and returns to the doctor with a message', async () => {
      await openEdit();
      await type('doctor-name-en', 'Ahmed A. Ali');
      await check('doctor-specialty-11'); // unchecked: only Cardiology stays

      await submit();
      const request = http.expectOne({ method: 'PUT', url: `${DOCTORS_URL}/7` });
      expect(request.request.body).toEqual({
        nameAr: 'أحمد علي',
        nameEn: 'Ahmed A. Ali',
        specialtyIds: [10],
        rowVersion: AHMED.rowVersion,
      });
      request.flush({ ...AHMED, nameEn: 'Ahmed A. Ali' });
      await settle();

      expect(router.url).toBe('/doctors/7');
      expect(TestBed.inject(DoctorsSession).takeFlash()).toBe('doctors.flash.updated');
    });

    it('a 409 conflict keeps the input, disables Save until Reload, then shows the earlier entries', async () => {
      await openEdit();
      await type('doctor-name-en', 'Mine');

      await submit();
      flushProblem(http.expectOne({ method: 'PUT', url: `${DOCTORS_URL}/7` }), 409, 'error.concurrency.conflict');
      await settle();

      expect(text('#doctor-form-conflict')).toContain(dar.conflict.title);
      expect(one<HTMLButtonElement>('#doctor-form-save')!.disabled).toBe(true);
      expect(one<HTMLInputElement>('#doctor-name-en')!.value).toBe('Mine');

      one<HTMLButtonElement>('#doctor-form-reload')!.click();
      await settle();
      http.expectOne(`${DOCTORS_URL}/7`).flush({ ...AHMED, nameEn: 'Theirs', rowVersion: 'AAAAAAAAAAk=' });
      await settle();

      expect(one<HTMLInputElement>('#doctor-name-en')!.value).toBe('Theirs');
      expect(one<HTMLButtonElement>('#doctor-form-save')!.disabled).toBe(false);
      expect(text('#doctor-form-earlier')).toContain('Mine');
      expect(text('#doctor-form-earlier')).toContain('قلب');
    });
  });
});
