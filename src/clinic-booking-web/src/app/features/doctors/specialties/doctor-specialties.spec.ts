import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { CurrentUser } from '../../../../api/auth-api';
import { Doctor } from '../../../../api/doctors-api';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import {
  AHMED,
  doctor,
  DOCTORS_URL,
  managerOf,
  referencePage,
  SPECIALTIES,
  SPECIALTIES_URL,
} from '../../../../testing/doctors-testing';
import { toastKeys } from '../../../../testing/toast-testing';
import { arabicTranslations as ar, doctorsArabic as dar } from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { DoctorsSession } from '../doctors-session';
import { DoctorSpecialtiesPage } from './doctor-specialties';

const ROUTES: Routes = [
  { path: 'doctors/:id/specialties', component: DoctorSpecialtiesPage },
  { path: '**', component: Stub },
];

const URL = `${DOCTORS_URL}/7`;

describe('DoctorSpecialtiesPage (D64)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const box = (id: number) => one<HTMLInputElement>(`#doctor-specialty-${id}`)!;

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
    await harness.navigateByUrl('/doctors/7/specialties', DoctorSpecialtiesPage);
    await settle();
    http.expectOne((r) => r.url === SPECIALTIES_URL).flush(referencePage(SPECIALTIES));
    http.expectOne(URL).flush(record);
    await settle();
  }

  async function toggle(id: number): Promise<void> {
    box(id).checked = !box(id).checked;
    box(id).dispatchEvent(new Event('change'));
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

  it('someone who manages none of the doctor\'s clinics is told so, without a form', async () => {
    await open(AHMED, managerOf(3));

    expect(text('#doctor-specialties-not-allowed')).toContain(dar.form.not_allowed);
    expect(one('form')).toBeNull();
  });

  it('shows the checkbox list with the doctor\'s specialties checked, including one beyond the first 100', async () => {
    await open(doctor(7, 'أحمد', 'Ahmed', { specialties: [{ id: 10, nameAr: 'قلب', nameEn: 'Cardiology' }, { id: 999, nameAr: 'نادر', nameEn: 'Rare' }] }));

    expect(text('h2')).toContain('أحمد');
    expect(box(10).checked).toBe(true);
    expect(box(999).checked).toBe(true);
    expect(box(12).checked).toBe(false);
  });

  it('saves through the doctor PUT with the names unchanged and the row version, then returns to the list with a toast', async () => {
    TestBed.inject(DoctorsSession).lastQuery.set({ page: '2' });
    await open();
    await toggle(11); // remove Dermatology
    await toggle(12); // add Paediatrics

    await submit();
    const request = http.expectOne({ method: 'PUT', url: URL });
    expect(request.request.body).toEqual({
      nameAr: 'أحمد علي',
      nameEn: 'Ahmed Ali',
      specialtyIds: [10, 12],
      rowVersion: AHMED.rowVersion,
    });
    request.flush(AHMED);
    await settle();

    expect(router.url).toBe('/doctors?page=2');
    expect(toastKeys()).toContain('doctors.flash.specialties_updated');
  });

  it('at least one: an empty choice is refused on the list and nothing is sent', async () => {
    await open();
    await toggle(10);
    await toggle(11);

    await submit();

    http.expectNone({ method: 'PUT', url: URL });
    expect(text('#doctor-specialties-field-error')).toBe(ar.error.doctor.specialties_required);
  });

  it('error.doctor.specialty_unavailable goes on the list', async () => {
    await open();
    await toggle(12);

    await submit();
    flushProblem(http.expectOne({ method: 'PUT', url: URL }), 400, 'error.validation.failed', {
      body: { errors: { specialtyIds: ['error.doctor.specialty_unavailable'] } },
    });
    await settle();

    expect(text('#doctor-specialties-field-error')).toBe(ar.error.doctor.specialty_unavailable);
  });

  it('a 409 conflict keeps the choice, disables Save until Reload, then lists the earlier choice', async () => {
    await open();
    await toggle(12);

    await submit();
    flushProblem(http.expectOne({ method: 'PUT', url: URL }), 409, 'error.concurrency.conflict');
    await settle();

    expect(text('#doctor-specialties-conflict')).toContain(dar.conflict.title);
    expect(one<HTMLButtonElement>('#doctor-specialties-save')!.disabled).toBe(true);
    expect(box(12).checked).toBe(true);

    one<HTMLButtonElement>('#doctor-specialties-reload')!.click();
    await settle();
    http.expectOne(URL).flush({ ...AHMED, nameEn: 'Theirs', rowVersion: 'AAAAAAAAAAk=' });
    await settle();

    expect(box(12).checked).toBe(false);
    expect(one<HTMLButtonElement>('#doctor-specialties-save')!.disabled).toBe(false);
    expect(text('#doctor-specialties-earlier')).toContain('أطفال');

    // The next save carries the reloaded names and row version.
    await submit();
    const request = http.expectOne({ method: 'PUT', url: URL });
    expect(request.request.body).toEqual({ nameAr: 'أحمد علي', nameEn: 'Theirs', specialtyIds: [11, 10], rowVersion: 'AAAAAAAAAAk=' });
    request.flush(AHMED);
    await settle();
  });
});
