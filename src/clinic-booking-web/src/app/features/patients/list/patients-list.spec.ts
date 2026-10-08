import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { installDialogPolyfill } from '../../../../testing/dialog-polyfill';
import { ALL_PATIENT_PERMISSIONS, JOHN, pageOfPatients, patient, PATIENTS_URL, SARA } from '../../../../testing/patients-testing';
import { patientsArabic as par } from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { PatientsList } from './patients-list';

const ROUTES: Routes = [
  { path: 'patients', component: PatientsList },
  { path: '**', component: Stub },
];

describe('PatientsList (D63)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const all = <T extends Element>(selector: string) => [...root().querySelectorAll<T>(selector)];
  const isList = (r: { url: string; method: string }) => r.url === PATIENTS_URL && r.method === 'GET';
  const dialog = () => one<HTMLDialogElement>('dialog')!;
  const dialogButtons = () => [...dialog().querySelectorAll<HTMLButtonElement>(':scope > div:last-of-type button')];

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  async function open(url = '/patients', permissions = ALL_PATIENT_PERMISSIONS): Promise<void> {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
    await signIn(permissions);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load('patients/ar');
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, PatientsList);
    await settle();
  }

  async function openWith(items = [SARA, JOHN], permissions = ALL_PATIENT_PERMISSIONS): Promise<void> {
    await open('/patients', permissions);
    http.expectOne(isList).flush(pageOfPatients(items));
    await settle();
  }

  beforeEach(() => {
    installDialogPolyfill();
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('patients')] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  it('asks for name ascending by default and keeps a clean URL', async () => {
    await open();

    const params = http.expectOne(isList).request.params;
    expect(params.get('SortBy')).toBe('name');
    expect(params.get('SortDirection')).toBe('asc');
    expect(params.has('Search')).toBe(false);
    expect(router.url).toBe('/patients');
  });

  it('sends a phone search as typed: the server reads it as a phone (D63)', async () => {
    await open('/patients?q=010%20123');

    expect(http.expectOne(isList).request.params.get('Search')).toBe('010 123');
  });

  it('the one search box says it takes a name or a phone', async () => {
    await openWith();

    expect(one<HTMLInputElement>('#patients-search')!.placeholder).toBe(par.search.placeholder);
  });

  it('shows the name with dir="auto", the phone formatted left to right, and the date', async () => {
    await openWith();

    const [first] = all<HTMLTableRowElement>('table tbody tr');
    const name = first.querySelector('th bdi')!;
    expect(name.getAttribute('dir')).toBe('auto');
    expect(name.textContent).toBe('سارة محمود');
    const phone = first.querySelector('td bdi')!;
    expect(phone.getAttribute('dir')).toBe('ltr');
    expect(phone.textContent).toBe('010 1234 5678');
    expect(first.querySelectorAll('td')[1].textContent?.trim()).not.toBe('');
  });

  it('cards carry a tel: link with the stored number; the table has none', async () => {
    await openWith();

    expect(all('table a[href^="tel:"]')).toHaveLength(0);
    expect(all<HTMLAnchorElement>('ul a[href^="tel:"]').map((a) => a.getAttribute('href'))).toEqual([
      'tel:+201012345678',
      'tel:+442079460958',
    ]);
  });

  it('a name containing HTML is shown as text', async () => {
    await openWith([patient(9, '<img src=x onerror=alert(1)>', '+201012345678')]);

    expect(one('table tbody img')).toBeNull();
    expect(one('table tbody th')!.textContent).toContain('<img');
  });

  it('with patients.read only, there is no New, Edit or Delete', async () => {
    await openWith([SARA], ['patients.read']);

    expect(one('a[href="/patients/new"]')).toBeNull();
    expect(one('a[href="/patients/1/edit"]')).toBeNull();
    expect(all('table button, ul button').filter((b) => b.textContent?.trim() === par.delete.action)).toHaveLength(0);
  });

  it('each action follows its own permission', async () => {
    await openWith([SARA], ['patients.read', 'patients.edit']);

    expect(one('a[href="/patients/new"]')).toBeNull();
    expect(one('table a[href="/patients/1/edit"]')).not.toBeNull();
    expect(all('table button').filter((b) => b.textContent?.trim() === par.delete.action)).toHaveLength(0);
  });

  it('Delete asks first, names the patient, and an error stays in the dialog', async () => {
    await openWith([SARA]);

    one<HTMLButtonElement>('table tbody button')!.click();
    await settle();
    expect(dialog().open).toBe(true);
    expect(dialog().textContent).toContain('سارة محمود');
    expect(document.activeElement).toBe(dialogButtons()[0]);

    dialogButtons()[1].click();
    await settle();
    flushProblem(http.expectOne({ method: 'DELETE', url: `${PATIENTS_URL}/1` }), 403, 'error.auth.forbidden');
    await settle();
    expect(dialog().open).toBe(true);
    expect(dialog().textContent).not.toContain('corr-123'); // a keyed failure has no reference (D52)

    dialogButtons()[1].click();
    await settle();
    http.expectOne({ method: 'DELETE', url: `${PATIENTS_URL}/1` }).flush(null);
    await settle();
    http.expectOne(isList).flush(pageOfPatients([]));
    await settle();
    expect(dialog().open).toBe(false);
    expect(root().textContent).toContain(par.flash.deleted);
  });
});
