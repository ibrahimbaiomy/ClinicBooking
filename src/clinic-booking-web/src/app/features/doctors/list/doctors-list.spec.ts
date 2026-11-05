import { HttpTestingController } from '@angular/common/http/testing';
import rootAr from '../../../../../public/i18n/ar.json';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { installDialogPolyfill } from '../../../../testing/dialog-polyfill';
import { toastKeys } from '../../../../testing/toast-testing';
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
import { arabicTranslations as ar, doctorsArabic as dar, doctorsEnglish as den } from '../../../../testing/transloco-testing';
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

  const filterButton = () => one<HTMLButtonElement>('cb-filter-panel > button')!;

  async function openFilters(): Promise<void> {
    filterButton().click();
    await settle();
  }

  async function applyFilters(): Promise<void> {
    one<HTMLButtonElement>('.filter-apply')!.click();
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
    await openFilters();
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
    await openFilters();
    expect(one<HTMLSelectElement>('#doctors-status')!.disabled).toBe(true);
    expect(text('#doctors-status-hint')).toBe(dar.filter.status_hint);
  });

  it('choosing a clinic in the panel enables the status filter; going back to all clinics resets it; nothing applies before Apply', async () => {
    await open('/doctors');
    http.expectOne(isDoctors).flush(pageOfDoctors([]));
    await settle();

    await openFilters();
    await choose('#doctors-clinic', '2');
    expect(one<HTMLSelectElement>('#doctors-status')!.disabled).toBe(false);
    await choose('#doctors-status', 'active');
    http.expectNone(isDoctors);
    expect(router.url).toBe('/doctors');

    await applyFilters();
    const active = http.expectOne(isDoctors);
    expect(active.request.params.get('ClinicId')).toBe('2');
    expect(active.request.params.get('IsActive')).toBe('true');
    active.flush(pageOfDoctors([]));
    await settle();
    expect(router.url).toBe('/doctors?clinic=2&active=active');
    expect(filterButton().textContent?.trim()).toBe(rootAr.filter.button_count.replace('{{ count }}', '2'));

    await openFilters();
    expect(one<HTMLSelectElement>('#doctors-clinic')!.value).toBe('2');
    expect(one<HTMLSelectElement>('#doctors-status')!.value).toBe('active');
    await choose('#doctors-clinic', '');
    expect(one<HTMLSelectElement>('#doctors-status')!.disabled).toBe(true);
    expect(one<HTMLSelectElement>('#doctors-status')!.value).toBe('all');
    await applyFilters();
    const cleared = http.expectOne(isDoctors);
    expect(cleared.request.params.has('IsActive')).toBe(false);
    expect(cleared.request.params.has('ClinicId')).toBe(false);
    cleared.flush(pageOfDoctors([]));
    await settle();
    expect(router.url).toBe('/doctors');
    expect(filterButton().textContent?.trim()).toBe(rootAr.filter.button);
  });

  it('applying filters returns to page 1', async () => {
    await open('/doctors?page=3');
    http.expectOne(isDoctors).flush(pageOfDoctors([], 100, 3));
    await settle();

    await openFilters();
    await choose('#doctors-specialty', '10');
    await applyFilters();

    expect(http.expectOne(isDoctors).request.params.get('Page')).toBe('1');
    expect(router.url).toBe('/doctors?specialty=10');
  });

  describe('the filter panel (D64)', () => {
    it('the button shows the active count and aria-expanded; the search stays outside the panel', async () => {
      await open('/doctors?q=x&specialty=11&clinic=2&active=inactive');
      http.expectOne(isDoctors).flush(pageOfDoctors([]));
      await settle();
      document.body.appendChild(root());

      expect(filterButton().textContent?.trim()).toBe(rootAr.filter.button_count.replace('{{ count }}', '3'));
      expect(filterButton().getAttribute('aria-expanded')).toBe('false');
      expect(one('#doctors-filters')).toBeNull();
      expect(one('cb-filter-panel #doctors-search')).toBeNull();
      expect(one('#doctors-search')).not.toBeNull();

      await openFilters();
      expect(filterButton().getAttribute('aria-expanded')).toBe('true');
      expect(filterButton().getAttribute('aria-controls')).toBe('doctors-filters');
      expect(one('#doctors-filters')).not.toBeNull();
      expect(document.activeElement).toBe(one('#doctors-specialty'));
      root().remove();
    });

    it('Clear resets every filter and applies at once, keeping the search', async () => {
      await open('/doctors?q=x&specialty=11&clinic=2&active=inactive');
      http.expectOne(isDoctors).flush(pageOfDoctors([]));
      await settle();

      await openFilters();
      one<HTMLButtonElement>('.filter-clear')!.click();
      await settle();

      const params = http.expectOne(isDoctors).request.params;
      expect(params.has('SpecialtyId')).toBe(false);
      expect(params.has('ClinicId')).toBe(false);
      expect(params.get('Search')).toBe('x');
      expect(router.url).toBe('/doctors?q=x');
      expect(one('#doctors-filters')).toBeNull();
    });

    it('Esc closes the panel without applying and gives the focus back to the button', async () => {
      await open('/doctors');
      http.expectOne(isDoctors).flush(pageOfDoctors([]));
      await settle();
      document.body.appendChild(root());

      await openFilters();
      await choose('#doctors-specialty', '10');
      one('#doctors-specialty')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await settle();

      expect(one('#doctors-filters')).toBeNull();
      expect(document.activeElement).toBe(filterButton());
      expect(filterButton().getAttribute('aria-expanded')).toBe('false');
      http.expectNone(isDoctors);
      expect(router.url).toBe('/doctors');
      root().remove();
    });

    it('a click outside closes the panel and gives the focus back to the button; a click inside does not', async () => {
      await open('/doctors');
      http.expectOne(isDoctors).flush(pageOfDoctors([]));
      await settle();
      document.body.appendChild(root());

      await openFilters();
      one<HTMLElement>('#doctors-filters')!.click();
      await settle();
      expect(one('#doctors-filters')).not.toBeNull();

      one<HTMLElement>('h2')!.click();
      await settle();
      expect(one('#doctors-filters')).toBeNull();
      expect(document.activeElement).toBe(filterButton());
      http.expectNone(isDoctors);
      root().remove();
    });

    it('opens on the inline-start side on phones and the inline-end side from sm up (logical, mirrors in RTL)', async () => {
      await open('/doctors');
      http.expectOne(isDoctors).flush(pageOfDoctors([]));
      await settle();

      await openFilters();
      const classes = one<HTMLElement>('#doctors-filters')!.className;
      expect(classes).toContain('start-0');
      expect(classes).toContain('sm:end-0');
      expect(classes).toContain('max-w-[calc(100vw-2rem)]');
    });
  });

  it('sorts from the name and slot headers; the specialties, clinics and actions columns are not sortable (D64)', async () => {
    await open('/doctors?page=2', managerOf(1), 'en');
    http.expectOne(isDoctors).flush(pageOfDoctors([AHMED], 30, 2));
    await settle();

    const headers = all<HTMLTableCellElement>('thead th');
    expect(headers.map((th) => th.querySelector('cb-sort-button') !== null)).toEqual([true, false, false, true, false]);
    expect(headers.map((th) => th.querySelector('svg') !== null)).toEqual([true, false, false, true, false]);
    expect(headers[0].getAttribute('aria-sort')).toBe('ascending');

    headers[3].querySelector<HTMLButtonElement>('button')!.click();
    await settle();
    expect(router.url).toBe('/doctors?sort=slotMinutes');
    const params = http.expectOne(isDoctors).request.params;
    expect(params.get('SortBy')).toBe('slotMinutes');
    expect(params.get('Page')).toBe('1');
    await settle();
    expect(all<HTMLTableCellElement>('thead th')[3].getAttribute('aria-sort')).toBe('ascending');

    // In English the names column starts with the English name.
    all<HTMLTableCellElement>('thead th')[0].querySelector<HTMLButtonElement>('button')!.click();
    await settle();
    expect(router.url).toBe('/doctors?sort=nameEn');
    http.expectOne(isDoctors);
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
    expect(row.querySelector<HTMLAnchorElement>('a.row-clinics')!.getAttribute('href')).toBe('/doctors/7/clinics');
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
    await openFilters();

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

  describe('direct row actions (D64)', () => {
    const tableActions = () => [...one('table tbody tr')!.querySelectorAll<HTMLElement>('a, button')];
    const cardActions = () => [...one('ul.md\\:hidden > li')!.querySelectorAll<HTMLElement>('a, button')];
    const classes = (elements: HTMLElement[]) =>
      elements.map((element) => [...element.classList].find((name) => name.startsWith('row-')));
    const dialog = () => one<HTMLDialogElement>('dialog')!;
    const dialogButtons = () => [...dialog().querySelectorAll<HTMLButtonElement>(':scope > div:last-of-type button')];

    async function openWith(user: Parameters<typeof open>[1]): Promise<void> {
      await open('/doctors', user);
      http.expectOne(isDoctors).flush(pageOfDoctors([AHMED]));
      await settle();
    }

    beforeEach(() => installDialogPolyfill());

    it('a manager of every clinic of the doctor gets Edit, Specialties, Clinics and Delete, in the table and the cards', async () => {
      await openWith(managerOf(1, 2));

      expect(classes(tableActions())).toEqual(['row-edit', 'row-specialties', 'row-clinics', 'row-delete']);
      expect(classes(cardActions())).toEqual(['row-edit', 'row-specialties', 'row-clinics', 'row-delete']);
      expect(tableActions().map((a) => a.getAttribute('href'))).toEqual([
        '/doctors/7/edit',
        '/doctors/7/specialties',
        '/doctors/7/clinics',
        null,
      ]);
      expect(tableActions().map((a) => a.textContent?.trim())).toEqual([
        dar.actions.edit,
        dar.actions.specialties,
        dar.actions.clinics,
        dar.actions.delete,
      ]);
    });

    it('a manager of one of its clinics may edit, but not delete', async () => {
      await openWith(managerOf(2));

      expect(classes(tableActions())).toEqual(['row-edit', 'row-specialties', 'row-clinics']);
    });

    it('a manager of another clinic only, or nobody with doctors.manage, gets Clinics only (read-only there)', async () => {
      await openWith(managerOf(3));
      expect(classes(tableActions())).toEqual(['row-clinics']);
    });

    it('without doctors.manage anywhere: Clinics only', async () => {
      await openWith({ clinicPermissions: [] });
      expect(classes(tableActions())).toEqual(['row-clinics']);
    });

    it('the action row wraps (no fixed width), and every action is text', async () => {
      await openWith(managerOf(1, 2));
      const container = tableActions()[0].parentElement!;

      expect(container.className).toContain('flex-wrap');
      expect(tableActions().every((action) => (action.textContent?.trim() ?? '') !== '')).toBe(true);
    });

    it('Delete asks first, naming both names, focus on Cancel; success reloads the list with a toast', async () => {
      await openWith(managerOf(1, 2));

      tableActions()[3].click();
      await settle();
      expect(dialog().open).toBe(true);
      expect(dialog().textContent).toContain('أحمد علي');
      expect(dialog().textContent).toContain('Ahmed Ali');
      expect(document.activeElement).toBe(dialogButtons()[0]);

      dialogButtons()[1].click();
      await settle();
      http.expectOne({ method: 'DELETE', url: `${DOCTORS_URL}/7` }).flush(null);
      await settle();

      expect(dialog().open).toBe(false);
      expect(toastKeys()).toContain('doctors.flash.deleted');
      http.expectOne(isDoctors).flush(pageOfDoctors([]));
    });

    it('error.doctor.all_clinics_required stays in the dialog, translated; no toast', async () => {
      await openWith(managerOf(1, 2));

      tableActions()[3].click();
      await settle();
      dialogButtons()[1].click();
      await settle();
      flushProblem(http.expectOne({ method: 'DELETE', url: `${DOCTORS_URL}/7` }), 403, 'error.doctor.all_clinics_required');
      await settle();

      expect(dialog().open).toBe(true);
      expect(dialog().textContent).toContain(ar.error.doctor.all_clinics_required);
      expect(toastKeys()).toEqual([]);
    });

    it('a doctor already deleted elsewhere (404) closes the dialog with "already deleted" and reloads', async () => {
      await openWith(managerOf(1, 2));

      tableActions()[3].click();
      await settle();
      dialogButtons()[1].click();
      await settle();
      flushProblem(http.expectOne({ method: 'DELETE', url: `${DOCTORS_URL}/7` }), 404, 'error.doctor.not_found');
      await settle();

      expect(dialog().open).toBe(false);
      expect(toastKeys()).toContain('doctors.flash.already_deleted');
      http.expectOne(isDoctors).flush(pageOfDoctors([]));
    });
  });
});
