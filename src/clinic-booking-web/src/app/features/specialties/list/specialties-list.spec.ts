import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { SpecialtyPage } from '../../../../api/specialties-api';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { installDialogPolyfill } from '../../../../testing/dialog-polyfill';
import { CARDIOLOGY, DERMATOLOGY, item, pageOf, SPECIALTIES_URL } from '../../../../testing/specialties-testing';
import {
  arabicTranslations as ar,
  englishTranslations as en,
  specialtiesArabic as sar,
  specialtiesEnglish as sen,
} from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { SpecialtiesSession } from '../specialties-session';
import { SEARCH_DEBOUNCE_MS, SpecialtiesList } from './specialties-list';

const ROUTES: Routes = [
  { path: 'specialties', component: SpecialtiesList },
  { path: '**', component: Stub },
];

const MANAGE = ['specialties.manage'];

describe('SpecialtiesList', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const all = <T extends Element>(selector: string) => [...root().querySelectorAll<T>(selector)];
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim();
  const tableRows = () => all<HTMLTableRowElement>('table tbody tr');

  const isList = (r: { url: string; method: string }) => r.url === SPECIALTIES_URL && r.method === 'GET';
  const nextList = () => http.expectOne(isList);
  const noList = () => http.expectNone(isList);
  const flush = (request: TestRequest, body: SpecialtyPage) => request.flush(body);

  /** Not whenStable(): a pending HTTP request keeps the app unstable until the test flushes it. */
  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      harness.detectChanges();
      await Promise.resolve();
    }
    if (vi.isFakeTimers()) {
      await vi.advanceTimersByTimeAsync(0);
    } else {
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
    harness.detectChanges();
  }

  /** With fake timers Angular's scheduler cannot be awaited; drive it by hand. */
  async function settleFake(ms = 0): Promise<void> {
    await vi.advanceTimersByTimeAsync(ms);
    harness.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    harness.detectChanges();
  }

  async function open(url = '/specialties', permissions = MANAGE, language: 'ar' | 'en' = 'ar'): Promise<void> {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    await signIn(permissions);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load(`specialties/${language}`);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, SpecialtiesList);
    await settle();
  }

  function type(value: string, init: InputEventInit = {}): void {
    const input = one<HTMLInputElement>('#specialties-search')!;
    input.value = value;
    input.dispatchEvent(new InputEvent('input', init));
  }

  beforeEach(() => {
    installDialogPolyfill();
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('specialties')],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  describe('the query and the URL', () => {
    it('the default view asks for Arabic name ascending, page 1 of 20, and keeps a clean URL', async () => {
      await open('/specialties');

      const request = nextList();
      const params = request.request.params;
      expect(params.get('SortBy')).toBe('nameAr');
      expect(params.get('SortDirection')).toBe('asc');
      expect(params.get('Page')).toBe('1');
      expect(params.get('PageSize')).toBe('20');
      expect(params.has('Search')).toBe(false);
      expect(router.url).toBe('/specialties');
      flush(request, pageOf([CARDIOLOGY]));
    });

    it('reads search, page, size and sort from the URL', async () => {
      await open('/specialties?q=%D9%82%D9%84%D8%A8&page=2&size=10&sort=createdAt&dir=desc');

      const params = nextList().request.params;
      expect(params.get('Search')).toBe('قلب');
      expect(params.get('Page')).toBe('2');
      expect(params.get('PageSize')).toBe('10');
      expect(params.get('SortBy')).toBe('createdAt');
      expect(params.get('SortDirection')).toBe('desc');
      expect(one<HTMLInputElement>('#specialties-search')!.value).toBe('قلب');
    });

    it('ignores invalid URL values instead of sending them', async () => {
      await open('/specialties?page=-3&size=9999&sort=password&dir=sideways');

      const params = nextList().request.params;
      expect(params.get('Page')).toBe('1');
      expect(params.get('PageSize')).toBe('20');
      expect(params.get('SortBy')).toBe('nameAr');
      expect(params.get('SortDirection')).toBe('asc');
    });

    it('follows the URL when it changes from outside (back/forward, a pasted link)', async () => {
      await open('/specialties');
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();

      await router.navigateByUrl('/specialties?q=skin&page=2');
      await settle();

      const params = nextList().request.params;
      expect(params.get('Search')).toBe('skin');
      expect(params.get('Page')).toBe('2');
      expect(one<HTMLInputElement>('#specialties-search')!.value).toBe('skin');
    });
  });

  describe('rows', () => {
    it('shows the Arabic name first and prominent in the Arabic UI, with lang and dir on both names', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();

      const names = [...tableRows()[0].querySelectorAll('th bdi')];
      expect(names.map((n) => n.textContent)).toEqual(['القلب', 'Cardiology']);
      expect(names[0].getAttribute('lang')).toBe('ar');
      expect(names[0].getAttribute('dir')).toBe('rtl');
      expect(names[1].getAttribute('lang')).toBe('en');
      expect(names[1].getAttribute('dir')).toBe('ltr');
      expect(names[0].parentElement!.className).toContain('font-medium');
      expect(names[1].parentElement!.className).not.toContain('font-medium');
    });

    it('shows the English name first and prominent in the English UI', async () => {
      await open('/specialties', MANAGE, 'en');
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();

      const names = [...tableRows()[0].querySelectorAll('th bdi')];
      expect(names.map((n) => n.textContent)).toEqual(['Cardiology', 'القلب']);
      expect(names[0].getAttribute('lang')).toBe('en');
      expect(names[1].getAttribute('lang')).toBe('ar');
      expect(names[1].getAttribute('dir')).toBe('rtl');
    });

    it('switches the order when the language changes', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();

      TestBed.inject(LanguageService).set('en');
      await TestBed.inject(TranslocoService).load('specialties/en');
      await settle();

      expect(tableRows()[0].querySelector('th bdi')!.textContent).toBe('Cardiology');
    });

    it('renders a name containing HTML or a script as plain text (D28)', async () => {
      const hostile = '<script>alert(1)</script><b onclick="x()">bold</b>';
      await open();
      flush(nextList(), pageOf([item(9, hostile, '<img src=x onerror=alert(1)>')]));
      await settle();

      expect(one('table script')).toBeNull();
      expect(one('table b')).toBeNull();
      expect(one('table img')).toBeNull();
      expect(root().querySelector('script')).toBeNull();
      expect(tableRows()[0].textContent).toContain(hostile);
      expect(tableRows()[0].textContent).toContain('<img src=x onerror=alert(1)>');
    });

    it('formats the created date through the intl pipe (Latin digits, Cairo time)', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();

      expect(tableRows()[0].querySelector('td')!.textContent).toMatch(/15.*01.*2026|2026/);
      expect(tableRows()[0].querySelector('td')!.textContent).not.toMatch(/[٠-٩]/);
    });

    it('has table semantics: a caption, column headers with scope, row headers, aria-sort', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY, DERMATOLOGY]));
      await settle();

      expect(text('table caption')).toBe(sar.table.caption);
      const headers = all<HTMLElement>('table thead th');
      expect(headers.map((h) => h.getAttribute('scope'))).toEqual(['col', 'col', 'col']);
      expect(headers[0].getAttribute('aria-sort')).toBe('ascending');
      expect(headers[1].hasAttribute('aria-sort')).toBe(false);
      expect(all('table tbody th[scope=row]')).toHaveLength(2);
    });

    it('labels every control and announces the results count politely', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY, DERMATOLOGY], 2));
      await settle();

      for (const id of ['specialties-search', 'specialties-sort', 'specialties-size']) {
        expect(one(`label[for=${id}]`)?.textContent?.trim()).toBeTruthy();
      }
      const status = all('[role=status]').find((s) => s.textContent?.includes('1–2'));
      expect(status?.getAttribute('aria-live')).toBe('polite');
      expect(status?.textContent).toContain('2');
    });
  });

  describe('states', () => {
    it('shows loading, then the rows', async () => {
      await open();

      expect(text('[aria-busy=true]')).toContain(sar.loading);
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();

      expect(tableRows()).toHaveLength(1);
      expect(text('[aria-busy]')).not.toContain(sar.loading);
    });

    it('shows the "no specialties" empty state when there are none at all', async () => {
      await open();
      flush(nextList(), pageOf([], 0));
      await settle();

      expect(text('main, section')).toContain(sar.empty.none);
      expect(one('table')).toBeNull();
    });

    it('shows "no results" for a search, and Clear search removes the search from the URL', async () => {
      await open('/specialties?q=zzz');
      flush(nextList(), pageOf([], 0));
      await settle();

      expect(text('section')).toContain(sar.empty.no_results);

      all<HTMLButtonElement>('button').find((b) => b.textContent?.trim() === sar.search.clear)!.click();
      await settle();

      expect(router.url).toBe('/specialties');
      expect(nextList().request.params.has('Search')).toBe(false);
    });

    it('shows a translated error, never server text, and Retry re-issues the same query without a reload', async () => {
      await open('/specialties?page=2&sort=nameEn');
      const failed = nextList();
      flushProblem(failed, 500, 'Some English server text');
      await settle();

      expect(text('[role=alert]')).toContain(ar.error.unexpected);
      expect(text('[role=alert]')).not.toContain('Some English server text');
      expect(text('[role=alert]')).toContain('corr-123');

      const urlBefore = router.url;
      const searchBox = one('#specialties-search');
      all<HTMLButtonElement>('[role=alert] button').find((b) => b.textContent?.trim() === sar.retry)!.click();
      await settle();

      const retried = nextList();
      expect(retried.request.params.get('Page')).toBe('2');
      expect(retried.request.params.get('SortBy')).toBe('nameEn');
      expect(router.url).toBe(urlBefore);
      expect(one('#specialties-search')).toBe(searchBox); // the page itself was not rebuilt
      flush(retried, pageOf([CARDIOLOGY], 21, 2));
      await settle();

      expect(one('section [role=alert]')).toBeNull();
      expect(tableRows()).toHaveLength(1);
    });

    it('shows the network message when the server cannot be reached', async () => {
      await open();
      nextList().error(new ProgressEvent('error'));
      await settle();

      expect(text('[role=alert]')).toContain(ar.error.network);
    });
  });

  describe('search', () => {
    it('waits for the debounce, then sends the raw text and goes back to page 1', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'Date'] });
      await open('/specialties?page=3');
      flush(nextList(), pageOf([CARDIOLOGY], 60, 3));
      await settleFake();

      type('c');
      type('card');
      await settleFake(SEARCH_DEBOUNCE_MS - 1);
      noList();

      await settleFake(1);
      expect(router.url).toBe('/specialties?q=card');
      const params = nextList().request.params;
      expect(params.get('Search')).toBe('card');
      expect(params.get('Page')).toBe('1');
    });

    it('sends Arabic as typed (the API normalises it)', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'Date'] });
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settleFake();

      type('أحمد');
      await settleFake(SEARCH_DEBOUNCE_MS);

      expect(nextList().request.params.get('Search')).toBe('أحمد');
    });

    it('Enter searches immediately, without waiting', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'Date'] });
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settleFake();

      type('derm');
      one<HTMLFormElement>('form[role=search]')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settleFake();

      expect(nextList().request.params.get('Search')).toBe('derm');
    });

    it('does not search for blank text', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'Date'] });
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settleFake();

      type('   ');
      await settleFake(SEARCH_DEBOUNCE_MS * 2);

      noList();
      expect(router.url).toBe('/specialties');
    });

    it('does not send while an IME is composing, and sends when the composition ends', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'Date'] });
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settleFake();

      type('ق', { isComposing: true });
      await settleFake(SEARCH_DEBOUNCE_MS * 2);
      noList();

      one<HTMLInputElement>('#specialties-search')!.dispatchEvent(new Event('compositionend'));
      await settleFake(SEARCH_DEBOUNCE_MS);
      expect(nextList().request.params.get('Search')).toBe('ق');
    });

    it('ignores a stale response: the older request is cancelled and never shown', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'Date'] });
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settleFake();

      type('a');
      await settleFake(SEARCH_DEBOUNCE_MS);
      const older = nextList();
      type('ab');
      await settleFake(SEARCH_DEBOUNCE_MS);
      const newer = nextList();

      flush(newer, pageOf([item(5, 'جديد', 'Newer')]));
      await settleFake();

      expect(older.cancelled).toBe(true);
      expect(tableRows()).toHaveLength(1);
      expect(tableRows()[0].textContent).toContain('Newer');
    });
  });

  describe('sort, size and paging', () => {
    beforeEach(async () => {
      await open('/specialties?page=2');
      flush(nextList(), pageOf([CARDIOLOGY], 45, 2));
      await settle();
    });

    it('changing the sort field goes back to page 1 and updates the URL', async () => {
      const select = one<HTMLSelectElement>('#specialties-sort')!;
      select.value = 'createdAt';
      select.dispatchEvent(new Event('change'));
      await settle();

      expect(router.url).toBe('/specialties?sort=createdAt');
      const params = nextList().request.params;
      expect(params.get('SortBy')).toBe('createdAt');
      expect(params.get('Page')).toBe('1');
    });

    it('the direction button flips the direction', async () => {
      all<HTMLButtonElement>('form[role=search] button').find((b) => b.textContent?.trim() === sar.sort.asc)!.click();
      await settle();

      expect(router.url).toBe('/specialties?dir=desc');
      expect(nextList().request.params.get('SortDirection')).toBe('desc');
    });

    it('changing the page size goes back to page 1', async () => {
      const select = one<HTMLSelectElement>('#specialties-size')!;
      select.value = '50';
      select.dispatchEvent(new Event('change'));
      await settle();

      expect(router.url).toBe('/specialties?size=50');
      expect(nextList().request.params.get('PageSize')).toBe('50');
    });

    it('Next and Previous move between pages', async () => {
      const buttons = () => all<HTMLButtonElement>('cb-pager button');
      expect(text('cb-pager span')).toBe(ar.pager.page_of.replace('{{ page }}', '2').replace('{{ total }}', '3'));

      buttons()[1].click();
      await settle();
      expect(router.url).toBe('/specialties?page=3');
      flush(nextList(), pageOf([CARDIOLOGY], 45, 3));
      await settle();
      expect(buttons()[1].disabled).toBe(true);

      buttons()[0].click();
      await settle();
      expect(router.url).toBe('/specialties?page=2');
      expect(nextList().request.params.get('Page')).toBe('2');
    });

    it('disables Previous on the first page', async () => {
      await router.navigateByUrl('/specialties');
      await settle();
      flush(nextList(), pageOf([CARDIOLOGY], 45, 1));
      await settle();

      expect(all<HTMLButtonElement>('cb-pager button')[0].disabled).toBe(true);
    });

    it('steps back to the last page when the URL points past the end', async () => {
      await router.navigateByUrl('/specialties?page=9');
      await settle();
      flush(nextList(), pageOf([], 45, 9));
      await settle();

      expect(router.url).toBe('/specialties?page=3');
      expect(nextList().request.params.get('Page')).toBe('3');
    });
  });

  describe('delete', () => {
    const dialog = () => one<HTMLDialogElement>('dialog')!;
    const deleteButtons = () =>
      all<HTMLButtonElement>('table tbody button').filter((b) => b.textContent?.trim() === sar.delete.action);
    const dialogButton = (label: string) =>
      [...dialog().querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;

    async function askDelete(): Promise<void> {
      deleteButtons()[0].click();
      await settle();
    }

    it('opens a dialog naming the specialty in both languages, with focus on Cancel', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();

      await askDelete();

      expect(dialog().open).toBe(true);
      expect(dialog().textContent).toContain('القلب');
      expect(dialog().textContent).toContain('Cardiology');
      expect(dialog().getAttribute('aria-labelledby')).toBeTruthy();
      expect(document.activeElement).toBe(dialogButton(sar.delete.cancel));
    });

    it('Cancel closes without calling the API', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();
      await askDelete();

      dialogButton(sar.delete.cancel).click();
      await settle();

      expect(dialog().open).toBe(false);
      http.expectNone(`${SPECIALTIES_URL}/1`);
      noList();
    });

    it('Escape closes it', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();
      await askDelete();

      dialog().dispatchEvent(new Event('cancel', { cancelable: true }));
      await settle();

      expect(dialog().open).toBe(false);
      http.expectNone(`${SPECIALTIES_URL}/1`);
    });

    it('Confirm deletes, reloads the list and announces it', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY, DERMATOLOGY]));
      await settle();
      await askDelete();

      dialogButton(sar.delete.confirm).click();
      await settle();
      const request = http.expectOne(`${SPECIALTIES_URL}/1`);
      expect(request.request.method).toBe('DELETE');
      request.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      expect(dialog().open).toBe(false);
      flush(nextList(), pageOf([DERMATOLOGY]));
      await settle();
      expect(tableRows()).toHaveLength(1);
      expect(text('[role=status]')).toContain(sar.flash.deleted);
      expect(document.activeElement).toBe(one('h2'));
    });

    it('disables both buttons while deleting', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();
      await askDelete();

      dialogButton(sar.delete.confirm).click();
      await settle();

      expect(dialog().querySelectorAll('button:disabled')).toHaveLength(2);
      http.expectOne(`${SPECIALTIES_URL}/1`).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      nextList();
    });

    it('a 404 means it was already deleted: says so and reloads', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();
      await askDelete();

      dialogButton(sar.delete.confirm).click();
      await settle();
      flushProblem(http.expectOne(`${SPECIALTIES_URL}/1`), 404, 'error.specialty.not_found');
      await settle();

      expect(dialog().open).toBe(false);
      expect(text('[role=status]')).toContain(sar.flash.already_deleted);
      flush(nextList(), pageOf([], 0));
      await settle();
      expect(text('section')).toContain(sar.empty.none);
    });

    it('any other error stays in the dialog, translated, ready for a later error.specialty.in_use', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();
      await askDelete();

      dialogButton(sar.delete.confirm).click();
      await settle();
      flushProblem(http.expectOne(`${SPECIALTIES_URL}/1`), 409, 'error.brand.new_conflict');
      await settle();

      expect(dialog().open).toBe(true);
      expect(dialog().textContent).toContain(ar.error.unexpected);
      expect(dialog().textContent).not.toContain('error.brand.new_conflict');
      noList();
    });

    it('deleting the last row of the last page steps back one page', async () => {
      await open('/specialties?page=3&size=10');
      flush(nextList(), pageOf([item(21, 'آخر', 'Last')], 21, 3, 10));
      await settle();
      await askDelete();

      dialogButton(sar.delete.confirm).click();
      await settle();
      http.expectOne(`${SPECIALTIES_URL}/21`).flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      flush(nextList(), pageOf([], 20, 3, 10)); // page 3 no longer exists
      await settle();

      expect(router.url).toBe('/specialties?page=2&size=10');
      const params = nextList().request.params;
      expect(params.get('Page')).toBe('2');
      expect(params.get('PageSize')).toBe('10');
    });

    it('deleting the only row of page 1 shows the "no specialties" empty state', async () => {
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();
      await askDelete();

      dialogButton(sar.delete.confirm).click();
      await settle();
      http.expectOne(`${SPECIALTIES_URL}/1`).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      flush(nextList(), pageOf([], 0));
      await settle();

      expect(text('section')).toContain(sar.empty.none);
      expect(one('table')).toBeNull();
      expect(router.url).toBe('/specialties');
    });
  });

  describe('permissions (UX only: the API enforces them)', () => {
    it('with specialties.manage: New, Edit and Delete are shown', async () => {
      await open('/specialties', MANAGE);
      flush(nextList(), pageOf([CARDIOLOGY, DERMATOLOGY]));
      await settle();

      expect(all('a[href="/specialties/new"]')).toHaveLength(1);
      expect(all('table tbody a[href$="/edit"]')).toHaveLength(2);
      expect(all('table tbody button')).toHaveLength(2);
      expect(one('a[href="/specialties/1/edit"]')?.getAttribute('aria-label')).toContain('القلب');
    });

    it('without it: the list is readable but there are no create, edit or delete controls', async () => {
      await open('/specialties', []);
      flush(nextList(), pageOf([CARDIOLOGY, DERMATOLOGY]));
      await settle();

      expect(tableRows()).toHaveLength(2);
      expect(all('a[href="/specialties/new"]')).toHaveLength(0);
      expect(all('a[href$="/edit"]')).toHaveLength(0);
      expect(all('table tbody button')).toHaveLength(0);
      expect(all('ul li button')).toHaveLength(0);
    });
  });

  describe('after a save elsewhere', () => {
    it('shows the one-time message and moves focus to the heading', async () => {
      TestBed.inject(SpecialtiesSession).setFlash('specialties.flash.saved_created');
      await open();
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();

      expect(text('[role=status]')).toContain(sar.flash.saved_created);
      expect(document.activeElement).toBe(one('h2'));
      expect(TestBed.inject(SpecialtiesSession).takeFlash()).toBeNull();
    });

    it('remembers the list query so the form can return to it', async () => {
      await open('/specialties?q=x&page=2');
      flush(nextList(), pageOf([CARDIOLOGY], 40, 2));
      await settle();

      expect(TestBed.inject(SpecialtiesSession).lastQuery()).toEqual({ q: 'x', page: 2 });
    });
  });

  describe('English', () => {
    it('uses the English scope strings', async () => {
      await open('/specialties', MANAGE, 'en');
      flush(nextList(), pageOf([CARDIOLOGY]));
      await settle();

      expect(text('h2')).toBe(sen.title);
      expect(text('table caption')).toBe(sen.table.caption);
      expect(text('cb-pager span')).toBe(en.pager.page_of.replace('{{ page }}', '1').replace('{{ total }}', '1'));
    });
  });
});
