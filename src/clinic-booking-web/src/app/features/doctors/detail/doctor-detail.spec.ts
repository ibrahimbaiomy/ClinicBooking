import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { CurrentUser } from '../../../../api/auth-api';
import { Doctor } from '../../../../api/doctors-api';
import { flushProblem, meBody, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { installDialogPolyfill } from '../../../../testing/dialog-polyfill';
import { AHMED, doctor, DOCTORS_URL, managerOf, NILE_AT, NOOR_AT } from '../../../../testing/doctors-testing';
import { arabicTranslations as ar, doctorsArabic as dar } from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { DoctorsSession } from '../doctors-session';
import { cairoTomorrow } from '../slot';
import { DoctorDetail } from './doctor-detail';

const ROUTES: Routes = [
  { path: 'doctors/:id', component: DoctorDetail },
  { path: '**', component: Stub },
];

const URL = `${DOCTORS_URL}/7`;
const translate = (template: string, values: Record<string, string>) =>
  Object.entries(values).reduce((text, [name, value]) => text.replace(`{{ ${name} }}`, value), template);

describe('DoctorDetail (D62)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const all = <T extends Element>(selector: string) => [...root().querySelectorAll<T>(selector)];
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const row = (clinicId: number) => one<HTMLLIElement>(`li[data-clinic="${clinicId}"]`)!;
  const dialogs = () => all<HTMLDialogElement>('dialog');
  const dialogButtons = (dialog: HTMLDialogElement) => [...dialog.querySelectorAll<HTMLButtonElement>(':scope > div:last-of-type button')];

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  /** Opens /doctors/7 and answers the load. AHMED: Nile (active) and Al Noor (inactive). */
  async function open(record: Doctor = AHMED, user: Partial<CurrentUser> = managerOf(1, 2)): Promise<void> {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
    await signIn([], 'token-1', user);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load('doctors/ar');
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/doctors/7', DoctorDetail);
    await settle();
    http.expectOne(URL).flush(record);
    await settle();
  }

  async function click(element: HTMLElement | null | undefined): Promise<void> {
    element!.click();
    await settle();
  }

  beforeEach(() => {
    installDialogPolyfill();
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('doctors')] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  describe('the summary', () => {
    it('shows both names, the specialties sorted by the Arabic name, and the dates', async () => {
      await open();

      expect(text('h2')).toBe('أحمد علي');
      expect(root().textContent).toContain('Ahmed Ali');
      expect(all('#doctor-specialties li').map((li) => li.textContent?.trim())).toEqual(['جلدية', 'قلب']);
      expect(root().textContent).toContain(dar.detail.never_updated);
    });

    it('lists every assignment with its status in words', async () => {
      await open();

      expect(row(1).textContent).toContain(dar.assignment.active);
      expect(row(2).textContent).toContain(dar.assignment.inactive);
    });

    it('a missing doctor shows "not found"', async () => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
      await signIn([], 'token-1', managerOf(1));
      await TestBed.inject(LanguageService).load();
      await TestBed.inject(TranslocoService).load('doctors/ar');
      harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/doctors/7', DoctorDetail);
      await settle();
      flushProblem(http.expectOne(URL), 404, 'error.doctor.not_found');
      await settle();

      expect(text('h2')).toBe(dar.not_found.title);
    });
  });

  describe('what each user may do (canIn, UX only)', () => {
    it('a manager of every clinic of the doctor may edit, delete, and manage each row', async () => {
      await open(AHMED, managerOf(1, 2));

      expect(one('#doctor-edit')).not.toBeNull();
      expect(one('#doctor-delete')).not.toBeNull();
      expect(row(1).querySelector('.hours-link')).not.toBeNull();
      expect(row(1).querySelector('.deactivate')).not.toBeNull();
      expect(row(2).querySelector('.activate')).not.toBeNull();
      expect(one('#doctor-slot-change')).not.toBeNull();
    });

    it('a manager of one of its clinics may edit but not delete, and manages only that row', async () => {
      await open(AHMED, managerOf(2));

      expect(one('#doctor-edit')).not.toBeNull();
      expect(one('#doctor-delete')).toBeNull();
      expect(row(1).querySelector('.hours-link')).toBeNull();
      expect(row(1).querySelector('.deactivate')).toBeNull();
      expect(row(2).querySelector('.hours-link')!.getAttribute('href')).toBe('/doctors/7/clinics/2/hours');
      expect(row(2).querySelector('.activate')).not.toBeNull();
      expect(one('#doctor-slot-change')).not.toBeNull();
    });

    it('a manager of another clinic only may add the doctor there, nothing else', async () => {
      await open(AHMED, managerOf(3));

      expect(one('#doctor-edit')).toBeNull();
      expect(one('#doctor-delete')).toBeNull();
      expect(one('#doctor-slot-change')).toBeNull();
      expect(all('.hours-link, .activate, .deactivate')).toHaveLength(0);
      expect(one('#doctor-add-clinic')).not.toBeNull();
    });

    it('someone without doctors.manage anywhere only reads', async () => {
      await open(AHMED, { clinicPermissions: [] });

      expect(all('#doctor-edit, #doctor-delete, #doctor-slot-change, #doctor-add-clinic, .hours-link, .activate, .deactivate')).toHaveLength(0);
    });
  });

  describe('activate and deactivate', () => {
    it('Activate is immediate and shows the server state', async () => {
      await open();

      await click(row(2).querySelector<HTMLButtonElement>('.activate'));
      const request = http.expectOne(`${URL}/clinics/2/activate`);
      expect(request.request.method).toBe('POST');
      request.flush({ ...AHMED, clinics: [NILE_AT, NOOR_AT] });
      await settle();

      expect(row(2).textContent).toContain(dar.assignment.active);
      expect(text('[role="status"]')).toBe(dar.flash.clinic_activated);
    });

    it('a refused reactivation (422) is shown in that row and names the other clinic', async () => {
      await open();

      await click(row(2).querySelector<HTMLButtonElement>('.activate'));
      flushProblem(http.expectOne(`${URL}/clinics/2/activate`), 422, 'error.doctor.period_overlaps_other_clinic', {
        body: { conflictingClinicId: 1 },
      });
      await settle();

      expect(row(2).querySelector('.row-message')!.textContent?.trim()).toBe(
        translate(dar.detail.overlap_named, { clinic: 'عيادة النيل' }),
      );
      expect(row(1).querySelector('.row-message')!.textContent?.trim()).toBe('');
    });

    it('without a known name for the other clinic, the generic overlap message is shown', async () => {
      await open();

      await click(row(2).querySelector<HTMLButtonElement>('.activate'));
      flushProblem(http.expectOne(`${URL}/clinics/2/activate`), 422, 'error.doctor.period_overlaps_other_clinic', {
        body: { conflictingClinicId: 99 },
      });
      await settle();

      expect(row(2).querySelector('.row-message')!.textContent?.trim()).toBe(ar.error.doctor.period_overlaps_other_clinic);
    });

    it('Deactivate asks first, with the focus on Cancel; Cancel sends nothing', async () => {
      await open();

      await click(row(1).querySelector<HTMLButtonElement>('.deactivate'));
      const dialog = dialogs()[0];
      expect(dialog.open).toBe(true);
      expect(dialog.textContent).toContain('عيادة النيل');
      expect(document.activeElement).toBe(dialogButtons(dialog)[0]);

      await click(dialogButtons(dialog)[0]);
      expect(dialog.open).toBe(false);
      http.expectNone(`${URL}/clinics/1/deactivate`);
    });

    it('confirming deactivates and shows the row as inactive', async () => {
      await open();

      await click(row(1).querySelector<HTMLButtonElement>('.deactivate'));
      await click(dialogButtons(dialogs()[0])[1]);
      http.expectOne(`${URL}/clinics/1/deactivate`).flush({ ...AHMED, clinics: [{ ...NILE_AT, isActive: false }, { ...NOOR_AT, isActive: false }] });
      await settle();

      expect(dialogs()[0].open).toBe(false);
      expect(row(1).textContent).toContain(dar.assignment.inactive);
      expect(text('[role="status"]')).toBe(dar.flash.clinic_deactivated);
    });
  });

  describe('adding a clinic', () => {
    it('opening the picker refreshes the session and offers only managed clinics the doctor does not have', async () => {
      await open(doctor(7, 'أحمد', 'Ahmed', { clinics: [NILE_AT] }), managerOf(1));

      await click(one<HTMLButtonElement>('#doctor-add-clinic'));
      // A grant made after sign-in (clinics 2 and 3) is offered at once; clinic 1 is already assigned.
      http.expectOne('/api/auth/me').flush(meBody({ ...managerOf(1, 2, 3) }));
      await settle();

      const options = all<HTMLOptionElement>('#doctor-add-clinic-select option').map((o) => o.value);
      expect(options).toEqual(['', '2', '3']); // "choose" first, then by Arabic name: النور before الهرم
    });

    it('adds the chosen clinic and shows it', async () => {
      await open(doctor(7, 'أحمد', 'Ahmed', { clinics: [NILE_AT] }), managerOf(1, 2));

      await click(one<HTMLButtonElement>('#doctor-add-clinic'));
      http.expectOne('/api/auth/me').flush(meBody({ ...managerOf(1, 2) }));
      await settle();
      const select = one<HTMLSelectElement>('#doctor-add-clinic-select')!;
      select.value = '2';
      select.dispatchEvent(new Event('change'));
      await settle();
      one<HTMLFormElement>('#doctor-add-clinic-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();

      const request = http.expectOne(`${URL}/clinics/2`);
      expect(request.request.method).toBe('POST');
      request.flush(doctor(7, 'أحمد', 'Ahmed', { clinics: [NILE_AT, NOOR_AT] }));
      await settle();

      expect(row(2)).not.toBeNull();
      expect(text('[role="status"]')).toBe(dar.flash.clinic_added);
    });

    it('says so when there is no other clinic to add', async () => {
      await open(doctor(7, 'أحمد', 'Ahmed', { clinics: [NILE_AT] }), managerOf(1));

      await click(one<HTMLButtonElement>('#doctor-add-clinic'));
      http.expectOne('/api/auth/me').flush(meBody({ ...managerOf(1) }));
      await settle();

      expect(root().textContent).toContain(dar.detail.add_clinic_none);
      expect(one('#doctor-add-clinic-select')).toBeNull();
    });
  });

  describe('the slot duration', () => {
    it('shows the current length and the pending change with its date', async () => {
      await open(doctor(7, 'أحمد', 'Ahmed', { slotMinutes: 15, pendingSlotChange: { slotMinutes: 30, effectiveFrom: '2026-07-10' } }));

      expect(text('#doctor-slot-current')).toContain('15');
      expect(text('#doctor-slot-pending')).toContain('30');
      expect(text('#doctor-slot-pending')).toContain('2026');
    });

    it('the form says a new change replaces the pending one and the date starts tomorrow in Cairo', async () => {
      await open();

      await click(one<HTMLButtonElement>('#doctor-slot-change'));

      expect(text('#doctor-slot-form')).toContain(dar.detail.slot_replaces);
      expect(one<HTMLInputElement>('#doctor-slot-date')!.getAttribute('min')).toBe(cairoTomorrow());
      expect(all('#doctor-slot-minutes option')).toHaveLength(24);
    });

    it('a date before tomorrow is refused on the field without asking the API', async () => {
      await open();
      await click(one<HTMLButtonElement>('#doctor-slot-change'));
      const date = one<HTMLInputElement>('#doctor-slot-date')!;
      date.value = '2020-01-01';
      date.dispatchEvent(new Event('input'));
      await settle();

      one<HTMLFormElement>('#doctor-slot-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();

      http.expectNone(`${URL}/slot-durations`);
      expect(text('#doctor-slot-date-error')).toBe(ar.error.doctor.effective_from_not_future);
    });

    it('a 422 for the date goes on the date field; success shows the pending change', async () => {
      await open();
      await click(one<HTMLButtonElement>('#doctor-slot-change'));
      const date = one<HTMLInputElement>('#doctor-slot-date')!;
      date.value = '2099-01-01';
      date.dispatchEvent(new Event('input'));
      const minutes = one<HTMLSelectElement>('#doctor-slot-minutes')!;
      minutes.value = '30';
      minutes.dispatchEvent(new Event('input')); // a browser fires input and change on a select
      minutes.dispatchEvent(new Event('change'));
      await settle();

      one<HTMLFormElement>('#doctor-slot-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();
      const refused = http.expectOne(`${URL}/slot-durations`);
      expect(refused.request.body).toEqual({ slotMinutes: 30, effectiveFrom: '2099-01-01' });
      flushProblem(refused, 422, 'error.doctor.effective_from_not_future');
      await settle();
      expect(text('#doctor-slot-date-error')).toBe(ar.error.doctor.effective_from_not_future);

      // The server error stays on the field until the user changes it; then the form can be sent again.
      date.value = '2099-01-02';
      date.dispatchEvent(new Event('input'));
      await settle();
      one<HTMLFormElement>('#doctor-slot-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();
      http
        .expectOne(`${URL}/slot-durations`)
        .flush({ ...AHMED, pendingSlotChange: { slotMinutes: 30, effectiveFrom: '2099-01-02' } });
      await settle();

      expect(one('#doctor-slot-form')).toBeNull();
      expect(text('#doctor-slot-pending')).toContain('30');
      expect(text('[role="status"]')).toBe(dar.flash.slot_changed);
    });
  });

  describe('delete', () => {
    it('error.doctor.all_clinics_required stays in the dialog, translated', async () => {
      await open();

      await click(one<HTMLButtonElement>('#doctor-delete'));
      await click(dialogButtons(dialogs()[1])[1]);
      flushProblem(http.expectOne({ method: 'DELETE', url: URL }), 403, 'error.doctor.all_clinics_required');
      await settle();

      expect(dialogs()[1].open).toBe(true);
      expect(dialogs()[1].textContent).toContain(ar.error.doctor.all_clinics_required);
      expect(router.url).toBe('/doctors/7');
    });

    it('success returns to the remembered list with a one-time message', async () => {
      TestBed.inject(DoctorsSession).lastQuery.set({ q: 'x' });
      await open();

      await click(one<HTMLButtonElement>('#doctor-delete'));
      expect(document.activeElement).toBe(dialogButtons(dialogs()[1])[0]);
      await click(dialogButtons(dialogs()[1])[1]);
      http.expectOne({ method: 'DELETE', url: URL }).flush(null);
      await settle();

      expect(router.url).toBe('/doctors?q=x');
      expect(TestBed.inject(DoctorsSession).takeFlash()).toBe('doctors.flash.deleted');
    });

    it('a doctor already deleted elsewhere (404) leaves with "already deleted"', async () => {
      await open();

      await click(one<HTMLButtonElement>('#doctor-delete'));
      await click(dialogButtons(dialogs()[1])[1]);
      flushProblem(http.expectOne({ method: 'DELETE', url: URL }), 404, 'error.doctor.not_found');
      await settle();

      expect(router.url).toBe('/doctors');
      expect(TestBed.inject(DoctorsSession).takeFlash()).toBe('doctors.flash.already_deleted');
    });
  });
});
