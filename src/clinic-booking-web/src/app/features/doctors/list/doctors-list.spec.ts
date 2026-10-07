import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import {
  AHMED,
  CLINICS,
  CLINICS_URL,
  doctor,
  DOCTORS_URL,
  managerOf,
  pageOfDoctors,
  referencePage,
  SPECIALTIES,
  SPECIALTIES_URL,
} from '../../../../testing/doctors-testing';
import { doctorsArabic as dar, doctorsEnglish as den } from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { DoctorsList } from './doctors-list';

const ROUTES: Routes = [
  { path: 'doctors', component: DoctorsList },
  { path: '**', component: Stub },
];

describe('DoctorsList (D62)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const all = <T extends Element>(selector: string) => [...root().querySelectorAll<T>(selector)];
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const isDoctors = (r: { url: string; method: string }) => r.url === DOCTORS_URL && r.method === 'GET';

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  /** Opens the list and answers the two reference lists (specialties, clinics). */
  async function open(
    url = '/doctors',
    user = managerOf(1),
    language: 'ar' | 'en' = 'ar',
    references = { specialties: referencePage(SPECIALTIES), clinics: referencePage(CLINICS) },
  ): Promise<void> {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    await signIn([], 'token-1', user);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load(`doctors/${language}`);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, DoctorsList);
    await settle();
    http.expectOne((r) => r.url === SPECIALTIES_URL).flush(references.specialties);
    http.expectOne((r) => r.url === CLINICS_URL).flush(references.clinics);
    await settle();
  }

  async function choose(selector: string, value: string): Promise<void> {
    const select = one<HTMLSelectElement>(selector)!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    await settle();
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('doctors')] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  it('loads the reference lists once, with the largest page, for the filters', async () => {
    await open();

    http.expectOne(isDoctors).flush(pageOfDoctors([]));
    await settle();
    const specialtyLabels = all<HTMLOptionElement>('#doctors-specialty option').map((o) => o.textContent?.trim());
    // Sorted by the Arabic name in the Arabic UI; "all" first.
    expect(specialtyLabels).toEqual([dar.filter.every_specialty, 'أطفال', 'جلدية', 'قلب']);
    expect(all('#doctors-clinic option')).toHaveLength(CLINICS.length + 1);
  });

  it('reads every filter from the URL and sends them to the API', async () => {
    await open('/doctors?q=x&specialty=11&clinic=2&active=inactive&page=2');

    const params = http.expectOne(isDoctors).request.params;
    expect(params.get('Search')).toBe('x');
    expect(params.get('SpecialtyId')).toBe('11');
    expect(params.get('ClinicId')).toBe('2');
    expect(params.get('IsActive')).toBe('false');
    expect(params.get('Page')).toBe('2');
  });

  it('the status filter is disabled without a clinic and never sent', async () => {
    await open('/doctors?active=active');

    expect(http.expectOne(isDoctors).request.params.has('IsActive')).toBe(false);
    expect(one<HTMLSelectElement>('#doctors-status')!.disabled).toBe(true);
    expect(text('#doctors-status-hint')).toBe(dar.filter.status_hint);
  });

  it('choosing a clinic enables the status filter; going back to all clinics resets it', async () => {
    await open('/doctors');
    http.expectOne(isDoctors).flush(pageOfDoctors([]));

    await choose('#doctors-clinic', '2');
    http.expectOne(isDoctors).flush(pageOfDoctors([]));
    await settle();
    expect(router.url).toBe('/doctors?clinic=2');
    expect(one<HTMLSelectElement>('#doctors-status')!.disabled).toBe(false);

    await choose('#doctors-status', 'active');
    const active = http.expectOne(isDoctors);
    expect(active.request.params.get('IsActive')).toBe('true');
    active.flush(pageOfDoctors([]));
    await settle();
    expect(router.url).toBe('/doctors?clinic=2&active=active');

    await choose('#doctors-clinic', '');
    const cleared = http.expectOne(isDoctors);
    expect(cleared.request.params.has('IsActive')).toBe(false);
    expect(cleared.request.params.has('ClinicId')).toBe(false);
    cleared.flush(pageOfDoctors([]));
    await settle();
    expect(router.url).toBe('/doctors');
    expect(one<HTMLSelectElement>('#doctors-status')!.disabled).toBe(true);
  });

  it('a filter change returns to page 1', async () => {
    await open('/doctors?page=3');
    http.expectOne(isDoctors).flush(pageOfDoctors([], 100, 3));

    await choose('#doctors-specialty', '10');

    expect(http.expectOne(isDoctors).request.params.get('Page')).toBe('1');
    expect(router.url).toBe('/doctors?specialty=10');
  });

  it('shows both names, the specialties in the UI language order, the clinics with an inactive one in words, and the slot', async () => {
    await open();
    http.expectOne(isDoctors).flush(pageOfDoctors([AHMED]));
    await settle();

    const row = one<HTMLTableRowElement>('table tbody tr')!;
    const cells = row.querySelectorAll('th, td');
    expect(cells[0].textContent).toContain('أحمد علي');
    expect(cells[0].textContent).toContain('Ahmed Ali');
    expect([...cells[1].querySelectorAll('li')].map((li) => li.textContent?.trim())).toEqual(['جلدية', 'قلب']);
    const clinics = [...cells[2].querySelectorAll('li')].map((li) => li.textContent?.replace(/\s+/g, ' ').trim());
    expect(clinics).toEqual(['عيادة النور(' + dar.assignment.inactive + ')', 'عيادة النيل']);
    expect(cells[3].textContent).toContain('15');
    expect(row.querySelector<HTMLAnchorElement>('a')!.getAttribute('href')).toBe('/doctors/7');
  });

  it('in English the English names lead and sort', async () => {
    await open('/doctors', managerOf(1), 'en');
    http.expectOne(isDoctors).flush(pageOfDoctors([AHMED]));
    await settle();

    const cells = one('table tbody tr')!.querySelectorAll('th, td');
    expect(cells[0].querySelector('bdi')!.textContent).toBe('Ahmed Ali');
    expect([...cells[1].querySelectorAll('li')].map((li) => li.textContent?.trim())).toEqual(['Cardiology', 'Dermatology']);
    expect(cells[2].textContent).toContain(den.assignment.inactive);
  });

  it('a name containing HTML is shown as text', async () => {
    await open();
    http.expectOne(isDoctors).flush(pageOfDoctors([doctor(9, '<b>x</b>', '<img src=x>')]));
    await settle();

    expect(one('table tbody tr b')).toBeNull();
    expect(one('table tbody tr img')).toBeNull();
    expect(text('table tbody tr th')).toContain('<b>x</b>');
  });

  it('says when a reference list holds more than 100 entries', async () => {
    await open('/doctors', managerOf(1), 'ar', {
      specialties: referencePage(SPECIALTIES, 250),
      clinics: referencePage(CLINICS),
    });
    http.expectOne(isDoctors).flush(pageOfDoctors([]));
    await settle();

    expect(root().textContent).toContain(dar.filter.incomplete);
  });

  it('offers "New doctor" only to someone who manages doctors in some clinic', async () => {
    await open('/doctors', { clinicPermissions: [] });
    http.expectOne(isDoctors).flush(pageOfDoctors([]));
    await settle();

    expect(one('a[href="/doctors/new"]')).toBeNull();
  });

  it('offers "New doctor" to a manager of any clinic', async () => {
    await open('/doctors', managerOf(3));
    http.expectOne(isDoctors).flush(pageOfDoctors([]));
    await settle();

    expect(one('a[href="/doctors/new"]')).not.toBeNull();
  });

  it('"no results" clears the search and every filter', async () => {
    await open('/doctors?q=zz&clinic=1&active=active&specialty=10');
    http.expectOne(isDoctors).flush(pageOfDoctors([]));
    await settle();

    expect(root().textContent).toContain(dar.empty.no_results);
    one<HTMLButtonElement>('#doctors-clear')!.click();
    await settle();

    http.expectOne(isDoctors).flush(pageOfDoctors([]));
    expect(router.url).toBe('/doctors');
  });
});
