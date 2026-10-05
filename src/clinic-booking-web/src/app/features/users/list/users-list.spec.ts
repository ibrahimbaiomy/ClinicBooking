import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslocoScope, TranslocoService } from '@jsverse/transloco';
import { UserPage } from '../../../../api/users-api';
import { flushProblem, provideAuthTesting, signIn, Stub } from '../../../../testing/auth-testing';
import { pageOfUsers, user, USERS_URL } from '../../../../testing/users-testing';
import { arabicTranslations as ar, usersArabic as uar, usersEnglish as uen } from '../../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { SEARCH_DEBOUNCE_MS, UsersList } from './users-list';

const ROUTES: Routes = [
  { path: 'users', component: UsersList },
  { path: '**', component: Stub },
];

const ANN = user(1, 'ann.admin');
const BOB = user(2, 'bob', { isActive: false });
const CARL = user(3, 'carl', { mustChangePassword: true });

describe('UsersList (D57, D59)', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const all = <T extends Element>(selector: string) => [...root().querySelectorAll<T>(selector)];
  const one = <T extends Element>(selector: string) => root().querySelector<T>(selector);
  const text = (selector: string) => one(selector)?.textContent?.replace(/\s+/g, ' ').trim();
  const tableRows = () => all<HTMLTableRowElement>('table tbody tr');

  const isList = (r: { url: string; method: string }) => r.url === USERS_URL && r.method === 'GET';
  const nextList = () => http.expectOne(isList);
  const flush = (request: TestRequest, body: UserPage) => request.flush(body);

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

  async function open(url = '/users', language: 'ar' | 'en' = 'ar'): Promise<void> {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    await signIn(['users.manage']);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(TranslocoService).load(`users/${language}`);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, UsersList);
    await settle();
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(ROUTES), provideTranslocoScope('users')] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  describe('the query and the URL', () => {
    it('the default view asks for user name ascending, page 1 of 20, no status, and keeps a clean URL', async () => {
      await open();

      const request = nextList();
      expect(request.request.params.get('SortBy')).toBe('userName');
      expect(request.request.params.get('SortDirection')).toBe('asc');
      expect(request.request.params.get('Page')).toBe('1');
      expect(request.request.params.get('PageSize')).toBe('20');
      expect(request.request.params.has('Search')).toBe(false);
      expect(request.request.params.has('IsActive')).toBe(false);
      expect(router.url).toBe('/users');
      flush(request, pageOfUsers([ANN]));
    });

    it('reads search, status, page, size and sort from the URL', async () => {
      await open('/users?q=ann&status=disabled&page=2&size=10&sort=createdAt&dir=desc');

      const params = nextList().request.params;
      expect(params.get('Search')).toBe('ann');
      expect(params.get('IsActive')).toBe('false');
      expect(params.get('Page')).toBe('2');
      expect(params.get('PageSize')).toBe('10');
      expect(params.get('SortBy')).toBe('createdAt');
      expect(params.get('SortDirection')).toBe('desc');
      expect(one<HTMLInputElement>('#users-search')!.value).toBe('ann');
      expect(one<HTMLSelectElement>('#users-status')!.value).toBe('disabled');
    });

    it('ignores invalid URL values instead of sending them', async () => {
      await open('/users?page=-3&size=9999&sort=passwordHash&dir=sideways&status=maybe');

      const params = nextList().request.params;
      expect(params.get('Page')).toBe('1');
      expect(params.get('PageSize')).toBe('20');
      expect(params.get('SortBy')).toBe('userName');
      expect(params.get('SortDirection')).toBe('asc');
      expect(params.has('IsActive')).toBe(false);
    });

    it('follows the URL when it changes from outside', async () => {
      await open();
      flush(nextList(), pageOfUsers([ANN]));
      await settle();

      await router.navigateByUrl('/users?q=bob&status=active');
      await settle();

      const params = nextList().request.params;
      expect(params.get('Search')).toBe('bob');
      expect(params.get('IsActive')).toBe('true');
    });
  });

  describe('rows', () => {
    it('shows the user name left to right, a status in words, and the pending-password indicator', async () => {
      await open();
      flush(nextList(), pageOfUsers([ANN, BOB, CARL]));
      await settle();

      const rows = tableRows();
      expect(rows).toHaveLength(3);
      expect(rows[0].querySelector('th bdi')!.getAttribute('dir')).toBe('ltr');
      expect(rows[0].querySelector('th')!.textContent).toContain('ann.admin');
      // Status is text, not colour alone.
      expect(rows[0].textContent).toContain(uar.status.active);
      expect(rows[1].textContent).toContain(uar.status.disabled);
      expect(rows[1].textContent).not.toContain(uar.status.active);
      expect(rows[2].textContent).toContain(uar.status.active);
      expect(rows[2].textContent).toContain(uar.status.must_change);
      expect(rows[0].textContent).not.toContain(uar.status.must_change);
    });

    it('links each row to the user page, with an accessible name that includes the user', async () => {
      await open();
      flush(nextList(), pageOfUsers([ANN]));
      await settle();

      const link = tableRows()[0].querySelector<HTMLAnchorElement>('a')!;
      expect(link.getAttribute('href')).toBe('/users/1');
      expect(link.getAttribute('aria-label')).toBe(uar.open_named.replace('{{ name }}', 'ann.admin'));
    });

    it('has the table (md up) and a card list (below md) with the same users', async () => {
      await open();
      flush(nextList(), pageOfUsers([ANN, BOB]));
      await settle();

      expect(tableRows()).toHaveLength(2);
      expect(all('ul.md\\:hidden > li')).toHaveLength(2);
      expect(one('table')!.className).toContain('hidden');
      expect(one('table caption')!.textContent).toBe(uar.table.caption);
    });

    it('renders a user name containing HTML as plain text', async () => {
      const hostile = '<script>alert(1)</script>';
      await open();
      flush(nextList(), pageOfUsers([user(9, hostile)]));
      await settle();

      expect(one('table script')).toBeNull();
      expect(tableRows()[0].textContent).toContain(hostile);
    });

    it('shows the result range in the live region', async () => {
      await open('/users?page=2&size=10');
      flush(nextList(), pageOfUsers([ANN], 11, 2, 10));
      await settle();

      expect(text('[role=status]')).toBe(uar.results.replace('{{ from }}', '11').replace('{{ to }}', '11').replace('{{ total }}', '11'));
    });

    it('has no create restriction: the page itself needs users.manage, so the button is always there', async () => {
      await open();
      flush(nextList(), pageOfUsers([]));
      await settle();

      expect(one<HTMLAnchorElement>('a[href="/users/new"]')!.textContent?.trim()).toBe(uar.new);
    });
  });

  describe('the states', () => {
    it('loading', async () => {
      await open();

      expect(text('[aria-busy]')).toBe(uar.loading);
      flush(nextList(), pageOfUsers([ANN]));
    });

    it('no users at all', async () => {
      await open();
      flush(nextList(), pageOfUsers([]));
      await settle();

      expect(text('main, section')).toContain(uar.empty.none);
    });

    it('no results for a search or a filter, with a way to clear them', async () => {
      await open('/users?q=zzz&status=disabled');
      flush(nextList(), pageOfUsers([]));
      await settle();

      expect(root().textContent).toContain(uar.empty.no_results);
      one<HTMLButtonElement>('.border-dashed button')!.click();
      await settle();
      expect(router.url).toBe('/users');
      flush(nextList(), pageOfUsers([ANN]));
    });

    it('an error with Retry and a reference to quote when nobody can explain it', async () => {
      await open();
      flushProblem(nextList(), 500, 'error.unexpected');
      await settle();

      expect(text('[role=alert]')).toContain(ar.error.unexpected);
      expect(text('[role=alert]')).toContain('corr-123');

      all<HTMLButtonElement>('[role=alert] button')[0].click();
      await settle();
      flush(nextList(), pageOfUsers([ANN]));
      await settle();
      expect(tableRows()).toHaveLength(1);
    });
  });

  describe('search, filter, sort and paging', () => {
    it('typing is debounced, replaces the URL and returns to page 1', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'Date'] });
      await open('/users?page=3');
      flush(nextList(), pageOfUsers([ANN], 60, 3));
      await settleFake();

      const field = one<HTMLInputElement>('#users-search')!;
      field.value = 'an';
      field.dispatchEvent(new InputEvent('input'));
      field.value = 'ann';
      field.dispatchEvent(new InputEvent('input'));
      await settleFake(SEARCH_DEBOUNCE_MS - 1);
      http.expectNone(isList);
      await settleFake(1);

      const params = nextList().request.params;
      expect(params.get('Search')).toBe('ann');
      expect(params.get('Page')).toBe('1');
      expect(router.url).toBe('/users?q=ann');
    });

    it('Enter searches at once', async () => {
      await open();
      flush(nextList(), pageOfUsers([ANN]));
      await settle();

      const field = one<HTMLInputElement>('#users-search')!;
      field.value = 'bob';
      field.dispatchEvent(new InputEvent('input', { isComposing: true }));
      one<HTMLFormElement>('form[role=search]')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();

      expect(nextList().request.params.get('Search')).toBe('bob');
    });

    it('the status filter updates the URL and the query, and returns to page 1', async () => {
      await open('/users?page=2');
      flush(nextList(), pageOfUsers([ANN], 40, 2));
      await settle();

      const select = one<HTMLSelectElement>('#users-status')!;
      select.value = 'active';
      select.dispatchEvent(new Event('change'));
      await settle();

      expect(nextList().request.params.get('IsActive')).toBe('true');
      expect(router.url).toBe('/users?status=active');
    });

    it('sorting by date created, the direction button and the page size', async () => {
      await open();
      flush(nextList(), pageOfUsers([ANN]));
      await settle();

      const sort = one<HTMLSelectElement>('#users-sort')!;
      sort.value = 'createdAt';
      sort.dispatchEvent(new Event('change'));
      await settle();
      const sorted = nextList();
      expect(sorted.request.params.get('SortBy')).toBe('createdAt');
      flush(sorted, pageOfUsers([ANN]));
      await settle();

      one<HTMLButtonElement>('form[role=search] button[type=button]')!.click();
      await settle();
      expect(nextList().request.params.get('SortDirection')).toBe('desc');
    });

    it('the page size select offers 10, 20 and 50', async () => {
      await open();
      flush(nextList(), pageOfUsers([ANN]));
      await settle();

      expect(all<HTMLOptionElement>('#users-size option').map((o) => o.value)).toEqual(['10', '20', '50']);
    });

    it('a late response for an older query is never shown', async () => {
      await open('/users?q=a');
      const first = nextList();
      await router.navigateByUrl('/users?q=b');
      await settle();
      const second = nextList();

      flush(second, pageOfUsers([BOB]));
      await settle();
      expect(tableRows()[0].textContent).toContain('bob');
      expect(first.cancelled).toBe(true);
    });

    it('a page past the end steps back to the last page', async () => {
      await open('/users?page=9');
      flush(nextList(), pageOfUsers([], 25, 9));
      await settle();

      expect(router.url).toBe('/users?page=2');
      flush(nextList(), pageOfUsers([ANN], 25, 2));
    });
  });

  it('works in English, left to right', async () => {
    await open('/users', 'en');
    flush(nextList(), pageOfUsers([ANN, BOB]));
    await settle();

    expect(text('h2')).toBe(uen.title);
    expect(tableRows()[1].textContent).toContain(uen.status.disabled);
  });
});
