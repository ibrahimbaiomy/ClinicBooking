import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { CurrentUser } from '../../../../api/auth-api';
import { Doctor } from '../../../../api/doctors-api';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { AHMED, doctor, DOCTORS_URL, managerOf } from '../../../../testing/doctors-testing';
import { toastKeys } from '../../../../testing/toast-testing';
import { arabicTranslations as ar, doctorsArabic as dar } from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { DoctorsSession } from '../doctors-session';
import { cairoTomorrow } from '../slot';
import { DoctorEditPage } from './doctor-edit';

const ROUTES: Routes = [
  { path: 'doctors/:id/edit', component: DoctorEditPage },
  { path: '**', component: Stub },
];

const URL = `${DOCTORS_URL}/7`;

describe('DoctorEditPage (D64)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const all = <T extends Element>(selector: string) => [...root().querySelectorAll<T>(selector)];
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  async function open(record: Doctor = AHMED, user: Partial<CurrentUser> = managerOf(1)): Promise<void> {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
    await signIn([], 'token-1', user);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load('doctors/ar');
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/doctors/7/edit', DoctorEditPage);
    await settle();
    http.expectOne(URL).flush(record);
    await settle();
  }

  async function type(id: string, value: string, event = 'input'): Promise<void> {
    const input = one<HTMLInputElement | HTMLSelectElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event(event));
    if (input instanceof HTMLSelectElement) input.dispatchEvent(new Event('change'));
    await settle();
  }

  async function submit(formId: string): Promise<void> {
    one<HTMLFormElement>(`#${formId}`)!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('doctors')] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  it('someone who manages none of the doctor\'s clinics is told so, without a form', async () => {
    await open(AHMED, managerOf(3));

    expect(text('#doctor-edit-not-allowed')).toContain(dar.form.not_allowed);
    expect(one('form')).toBeNull();
  });

  it('a manager of any one of its clinics edits the names, prefilled; there are no specialty or clinic fields', async () => {
    await open(AHMED, managerOf(2));

    expect(one<HTMLInputElement>('#doctor-name-ar')!.value).toBe('أحمد علي');
    expect(one<HTMLInputElement>('#doctor-name-en')!.value).toBe('Ahmed Ali');
    expect(all('input[type=checkbox]')).toHaveLength(0);
  });

  describe('names', () => {
    it('saves through the doctor PUT with the row version and the current specialties unchanged, then returns to the list with a toast', async () => {
      TestBed.inject(DoctorsSession).lastQuery.set({ q: 'x' });
      await open();
      await type('doctor-name-en', ' Ahmed A. Ali ');

      await submit('doctor-names-form');
      const request = http.expectOne({ method: 'PUT', url: URL });
      expect(request.request.body).toEqual({
        nameAr: 'أحمد علي',
        nameEn: 'Ahmed A. Ali',
        specialtyIds: [11, 10],
        rowVersion: AHMED.rowVersion,
      });
      request.flush({ ...AHMED, nameEn: 'Ahmed A. Ali' });
      await settle();

      expect(router.url).toBe('/doctors?q=x');
      expect(toastKeys()).toContain('doctors.flash.updated');
    });

    it('an empty name is refused on its field and nothing is sent', async () => {
      await open();
      await type('doctor-name-ar', '  ');

      await submit('doctor-names-form');

      http.expectNone({ method: 'PUT', url: URL });
      expect(text('#doctor-name-ar-error')).toBe(ar.error.doctor.name_ar_required);
    });

    it('a 409 conflict keeps the input, disables Save until Reload, then shows the earlier entries', async () => {
      await open();
      await type('doctor-name-en', 'Mine');

      await submit('doctor-names-form');
      flushProblem(http.expectOne({ method: 'PUT', url: URL }), 409, 'error.concurrency.conflict');
      await settle();

      expect(text('#doctor-edit-conflict')).toContain(dar.conflict.title);
      expect(one<HTMLButtonElement>('#doctor-names-save')!.disabled).toBe(true);
      expect(one<HTMLInputElement>('#doctor-name-en')!.value).toBe('Mine');
      expect(toastKeys()).toEqual([]);

      one<HTMLButtonElement>('#doctor-edit-reload')!.click();
      await settle();
      http.expectOne(URL).flush({ ...AHMED, nameEn: 'Theirs', rowVersion: 'AAAAAAAAAAk=', specialties: [{ id: 12, nameAr: 'أطفال', nameEn: 'Paediatrics' }] });
      await settle();

      expect(one<HTMLInputElement>('#doctor-name-en')!.value).toBe('Theirs');
      expect(one<HTMLButtonElement>('#doctor-names-save')!.disabled).toBe(false);
      expect(text('#doctor-edit-earlier')).toContain('Mine');

      // The next save carries the reloaded row version and the reloaded specialties.
      await submit('doctor-names-form');
      const request = http.expectOne({ method: 'PUT', url: URL });
      expect(request.request.body).toEqual({ nameAr: 'أحمد علي', nameEn: 'Theirs', specialtyIds: [12], rowVersion: 'AAAAAAAAAAk=' });
      request.flush(AHMED);
      await settle();
    });
  });

  describe('the slot duration (its own request and errors)', () => {
    it('shows the current length, the pending change, and a form that starts tomorrow in Cairo and replaces the pending change', async () => {
      await open(doctor(7, 'أحمد', 'Ahmed', { slotMinutes: 15, pendingSlotChange: { slotMinutes: 30, effectiveFrom: '2026-07-10' } }));

      expect(text('#doctor-slot-current')).toContain('15');
      expect(text('#doctor-slot-pending')).toContain('30');
      expect(text('#doctor-slot-form')).toContain(dar.detail.slot_replaces);
      expect(one<HTMLInputElement>('#doctor-slot-date')!.getAttribute('min')).toBe(cairoTomorrow());
      expect(all('#doctor-slot-minutes option')).toHaveLength(24);
      expect(one<HTMLSelectElement>('#doctor-slot-minutes')!.value).toBe('15');
    });

    it('a date before tomorrow is refused on the field without asking the API', async () => {
      await open();
      await type('doctor-slot-date', '2020-01-01');

      await submit('doctor-slot-form');

      http.expectNone(`${URL}/slot-durations`);
      expect(text('#doctor-slot-date-error')).toBe(ar.error.doctor.effective_from_not_future);
    });

    it('a 422 for the date goes on the date field; success returns to the list with a toast', async () => {
      await open();
      await type('doctor-slot-date', '2099-01-01');
      await type('doctor-slot-minutes', '30');

      await submit('doctor-slot-form');
      const refused = http.expectOne(`${URL}/slot-durations`);
      expect(refused.request.body).toEqual({ slotMinutes: 30, effectiveFrom: '2099-01-01' });
      flushProblem(refused, 422, 'error.doctor.effective_from_not_future');
      await settle();
      expect(text('#doctor-slot-date-error')).toBe(ar.error.doctor.effective_from_not_future);
      expect(router.url).toBe('/doctors/7/edit');

      await type('doctor-slot-date', '2099-01-02');
      await submit('doctor-slot-form');
      http.expectOne(`${URL}/slot-durations`).flush({ ...AHMED, pendingSlotChange: { slotMinutes: 30, effectiveFrom: '2099-01-02' } });
      await settle();

      expect(router.url).toBe('/doctors');
      expect(toastKeys()).toContain('doctors.flash.slot_changed');
    });

    it('is independent of the names: a names conflict does not block it, and its error does not touch the names', async () => {
      await open();
      await type('doctor-name-en', 'Mine');
      await submit('doctor-names-form');
      flushProblem(http.expectOne({ method: 'PUT', url: URL }), 409, 'error.concurrency.conflict');
      await settle();

      await type('doctor-slot-date', '2099-01-01');
      await submit('doctor-slot-form');
      flushProblem(http.expectOne(`${URL}/slot-durations`), 500, 'error.unexpected');
      await settle();

      expect(text('#doctor-slot-error')).toBe(ar.error.unexpected);
      expect(one('#doctor-edit-error')).toBeNull();
      expect(text('#doctor-edit-conflict')).toContain(dar.conflict.title);

      await submit('doctor-slot-form');
      http.expectOne(`${URL}/slot-durations`).flush(AHMED);
      await settle();
      expect(router.url).toBe('/doctors');
    });
  });
});
