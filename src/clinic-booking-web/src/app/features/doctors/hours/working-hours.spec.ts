import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { CurrentUser } from '../../../../api/auth-api';
import { WorkingHours } from '../../../../api/doctors-api';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { AHMED, DOCTORS_URL, hours, managerOf } from '../../../../testing/doctors-testing';
import { arabicTranslations as ar, doctorsArabic as dar } from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { WorkingHoursPage } from './working-hours';

const ROUTES: Routes = [
  { path: 'doctors/:id/clinics/:clinicId/hours', component: WorkingHoursPage },
  { path: '**', component: Stub },
];

const HOURS_URL = `${DOCTORS_URL}/7/clinics/1/working-hours`;

/** Monday 09:00–13:00 and 17:00–21:00, Saturday 10:00–12:00. */
const WEEK = hours([
  { dayOfWeek: 1, start: '17:00:00', end: '21:00:00' },
  { dayOfWeek: 6, start: '10:00:00', end: '12:00:00' },
  { dayOfWeek: 1, start: '09:00:00', end: '13:00:00' },
]);

describe('WorkingHoursPage (D62)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const all = <T extends Element>(selector: string) => [...root().querySelectorAll<T>(selector)];
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const day = (dayOfWeek: number) => one<HTMLFieldSetElement>(`fieldset[data-day="${dayOfWeek}"]`)!;
  const periodsOf = (dayOfWeek: number) => [...day(dayOfWeek).querySelectorAll<HTMLElement>('[data-period]')];
  const times = (period: HTMLElement) => [...period.querySelectorAll<HTMLInputElement>('input[type="time"]')];
  const errorsOf = (period: HTMLElement) => period.querySelector('.period-errors')!.textContent?.replace(/\s+/g, ' ').trim();

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  async function open(week: WorkingHours = WEEK, user: Partial<CurrentUser> = managerOf(1, 2)): Promise<void> {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
    await signIn([], 'token-1', user);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load('doctors/ar');
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/doctors/7/clinics/1/hours', WorkingHoursPage);
    await settle();
    http.expectOne(`${DOCTORS_URL}/7`).flush(AHMED);
    http.expectOne(HOURS_URL).flush(week);
    await settle();
  }

  async function setTime(input: HTMLInputElement, value: string): Promise<void> {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function save(): Promise<TestRequest | null> {
    one<HTMLFormElement>('#hours-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
    const [request] = http.match({ method: 'PUT', url: HOURS_URL });
    return request ?? null;
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('doctors')] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('the week', () => {
    it('names the doctor and the clinic and shows Saturday to Friday, each day by start', async () => {
      await open();

      expect(text('#hours-names')).toContain('أحمد علي');
      expect(text('#hours-names')).toContain('عيادة النيل');
      expect(all('fieldset legend').map((l) => l.textContent?.trim())).toEqual([
        dar.days.saturday,
        dar.days.sunday,
        dar.days.monday,
        dar.days.tuesday,
        dar.days.wednesday,
        dar.days.thursday,
        dar.days.friday,
      ]);
      expect(periodsOf(1).map((p) => times(p).map((t) => t.value))).toEqual([
        ['09:00', '13:00'],
        ['17:00', '21:00'],
      ]);
      expect(times(periodsOf(6)[0])[0].getAttribute('dir')).toBe('ltr');
      expect(day(0).textContent).toContain(dar.hours.no_periods);
    });

    it('says when the assignment is inactive', async () => {
      await open(hours([], { isActive: false }));

      expect(text('#hours-inactive')).toBe(dar.hours.inactive);
    });

    it('is read-only without doctors.manage in this clinic, even for a manager of the doctor\'s other clinic', async () => {
      await open(WEEK, managerOf(2));

      expect(text('#hours-read-only')).toBe(dar.hours.read_only);
      expect(all<HTMLInputElement>('input[type="time"]').every((input) => input.disabled)).toBe(true);
      expect(all('#hours-save, .remove, [id^="add-"]')).toHaveLength(0);
    });

    it('an unknown or unassigned clinic shows its own message', async () => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
      await signIn([], 'token-1', managerOf(1));
      await TestBed.inject(LanguageService).load();
      await TestBed.inject(TranslocoService).load('doctors/ar');
      harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/doctors/7/clinics/1/hours', WorkingHoursPage);
      await settle();
      http.expectOne(`${DOCTORS_URL}/7`).flush(AHMED);
      flushProblem(http.expectOne(HOURS_URL), 404, 'error.doctor.clinic_not_assigned');
      await settle();

      expect(text('#hours-not-found')).toContain(ar.error.doctor.clinic_not_assigned);
    });
  });

  describe('editing', () => {
    it('adds a period with the focus on its start, and removes one', async () => {
      await open();

      one<HTMLButtonElement>('#add-0')!.click();
      await settle();
      expect(periodsOf(0)).toHaveLength(1);
      expect(document.activeElement).toBe(times(periodsOf(0)[0])[0]);

      periodsOf(1)[0].querySelector<HTMLButtonElement>('.remove')!.click();
      await settle();
      expect(periodsOf(1).map((p) => times(p)[0].value)).toEqual(['17:00']);
    });

    it('an empty time is marked on its input and nothing is sent', async () => {
      await open();
      one<HTMLButtonElement>('#add-0')!.click();
      await settle();
      await setTime(times(periodsOf(0)[0])[0], '08:00');

      expect(await save()).toBeNull();
      expect(errorsOf(periodsOf(0)[0])).toBe(dar.hours.required);
      expect(times(periodsOf(0)[0])[1].getAttribute('aria-invalid')).toBe('true');
      expect(text('#hours-error')).toBe(dar.hours.invalid);
    });

    it('sends the whole week Saturday first, then by start, as "HH:mm", with the row version', async () => {
      await open();
      one<HTMLButtonElement>('#add-1')!.click(); // a Monday period added after the others, earliest in the day
      await settle();
      const added = periodsOf(1)[2];
      await setTime(times(added)[0], '07:00');
      await setTime(times(added)[1], '08:00');

      const request = (await save())!;

      expect(request.request.body).toEqual({
        periods: [
          { dayOfWeek: 6, start: '10:00', end: '12:00' },
          { dayOfWeek: 1, start: '07:00', end: '08:00' },
          { dayOfWeek: 1, start: '09:00', end: '13:00' },
          { dayOfWeek: 1, start: '17:00', end: '21:00' },
        ],
        rowVersion: WEEK.rowVersion,
      });
      request.flush(hours([{ dayOfWeek: 1, start: '07:00:00', end: '08:00:00' }], { rowVersion: 'AAAAAAAAAAM=' }));
      await settle();
      expect(text('[role="status"]')).toBe(dar.flash.hours_saved);
      expect(periodsOf(1)).toHaveLength(1);
    });

    it('field errors from the API go on the input of the period at that index of the request', async () => {
      await open();

      const request = (await save())!;
      // Index 2 of the request is Monday 17:00 (Saturday is index 0, Monday 09:00 index 1).
      flushProblem(request, 400, 'error.validation.failed', {
        body: { errors: { 'periods[2].end': ['error.doctor.period_end_not_after_start'] } },
      });
      await settle();

      expect(errorsOf(periodsOf(1)[1])).toBe(ar.error.doctor.period_end_not_after_start);
      expect(times(periodsOf(1)[1])[1].getAttribute('aria-invalid')).toBe('true');
      expect(errorsOf(periodsOf(1)[0])).toBe('');
      expect(errorsOf(periodsOf(6)[0])).toBe('');
    });

    it('a 422 with periodIndex is shown next to that period', async () => {
      await open();

      flushProblem((await save())!, 422, 'error.doctor.period_shorter_than_slot', { body: { periodIndex: 0 } });
      await settle();

      expect(errorsOf(periodsOf(6)[0])).toBe(ar.error.doctor.period_shorter_than_slot);
      expect(text('#hours-error')).toBe(dar.hours.invalid);
    });

    it('an overlap with another clinic names that clinic next to the period', async () => {
      await open();

      flushProblem((await save())!, 422, 'error.doctor.period_overlaps_other_clinic', {
        body: { periodIndex: 1, conflictingClinicId: 2 },
      });
      await settle();

      expect(errorsOf(periodsOf(1)[0])).toBe(dar.hours.other_clinic.replace('{{ clinic }}', 'عيادة النور'));
    });

    it('a 422 that names no period is form-level', async () => {
      await open();

      flushProblem((await save())!, 422, 'error.doctor.periods_overlap');
      await settle();

      expect(text('#hours-error')).toBe(ar.error.doctor.periods_overlap);
      expect(all('.period-errors').every((e) => e.textContent?.trim() === '')).toBe(true);
    });

    it('a 409 conflict disables Save until Reload, then shows the earlier entries', async () => {
      await open();
      await setTime(times(periodsOf(6)[0])[0], '09:30');

      flushProblem((await save())!, 409, 'error.concurrency.conflict');
      await settle();
      expect(text('#hours-conflict')).toContain(dar.conflict.title);
      expect(one<HTMLButtonElement>('#hours-save')!.disabled).toBe(true);

      one<HTMLButtonElement>('#hours-reload')!.click();
      await settle();
      http.expectOne(HOURS_URL).flush(hours([], { rowVersion: 'AAAAAAAAAAQ=' }));
      await settle();

      expect(one<HTMLButtonElement>('#hours-save')!.disabled).toBe(false);
      expect(periodsOf(6)).toHaveLength(0);
      expect(text('#hours-earlier')).toContain('09:30');
    });
  });
});
