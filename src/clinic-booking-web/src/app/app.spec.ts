import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { provideAuthTesting, signIn, tokenResponse } from '../testing/auth-testing';
import { arabicTranslations, englishTranslations } from '../testing/transloco-testing';
import { App } from './app';
import { routes } from './app.routes';
import { SessionService } from './core/auth/session.service';
import { LANGUAGE_STORAGE_KEY } from './core/i18n/language';
import { LanguageService } from './core/i18n/language.service';

async function render(url = '/', signedIn = true): Promise<ComponentFixture<App>> {
  if (signedIn) {
    await signIn(['specialties.manage']);
  }
  const fixture = TestBed.createComponent(App);
  await TestBed.inject(LanguageService).load();
  await TestBed.inject(Router).navigateByUrl(url);
  await fixture.whenStable();
  return fixture;
}

const text = (fixture: ComponentFixture<App>, selector: string) =>
  (fixture.nativeElement as HTMLElement).querySelector(selector)?.textContent?.trim();

const languageButton = (fixture: ComponentFixture<App>) =>
  (fixture.nativeElement as HTMLElement).querySelector('cb-language-switcher button') as HTMLButtonElement;

describe('App shell', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting(routes) });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('runs without zone.js', () => {
    expect('Zone' in globalThis).toBe(false);
  });

  it('shows the Arabic shell by default: title, user, switcher and placeholder, right to left', async () => {
    const fixture = await render();

    expect(text(fixture, 'h1')).toBe(arabicTranslations.app.title);
    expect(text(fixture, 'header strong')).toBe('someone');
    expect(text(fixture, 'cb-language-switcher button')).toBe(arabicTranslations.language.en);
    expect(text(fixture, 'main p')).toBe(arabicTranslations.shell.placeholder);
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
    expect(TestBed.inject(Title).getTitle()).toBe(arabicTranslations.app.title);
  });

  it('switching the language updates the UI, lang, dir and storage without zone.js', async () => {
    const fixture = await render();
    const button = languageButton(fixture);

    button.click();
    await fixture.whenStable();

    expect(text(fixture, 'h1')).toBe(englishTranslations.app.title);
    expect(text(fixture, 'cb-language-switcher button')).toBe(englishTranslations.language.ar);
    expect(text(fixture, 'main p')).toBe(englishTranslations.shell.placeholder);
    expect(document.documentElement.lang).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');
    expect(TestBed.inject(Title).getTitle()).toBe(englishTranslations.app.title);
    expect(button.getAttribute('lang')).toBe('ar');
    expect(button.getAttribute('aria-label')).toBe(englishTranslations.language.switch);

    button.click();
    await fixture.whenStable();

    expect(text(fixture, 'h1')).toBe(arabicTranslations.app.title);
    expect(document.documentElement.dir).toBe('rtl');
  });

  it('starts in English when English was saved', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en');

    const fixture = await render();

    expect(text(fixture, 'h1')).toBe(englishTranslations.app.title);
    expect(document.documentElement.dir).toBe('ltr');
  });

  it('signed in, an unknown link goes to the shell (the API serves index.html for it)', async () => {
    const fixture = await render('/nothing-here');

    expect(TestBed.inject(Router).url).toBe('/');
    expect(text(fixture, 'main p')).toBe(arabicTranslations.shell.placeholder);
  });

  it('signed in, the shell has a navigation entry to Specialties that marks the current page', async () => {
    const fixture = await render('/');
    await TestBed.inject(Router).navigateByUrl('/specialties');
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/specialties').flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const link = (fixture.nativeElement as HTMLElement).querySelector('nav a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/specialties');
    expect(link.textContent?.trim()).toBe(arabicTranslations.shell.nav.specialties);
    expect(link.getAttribute('aria-current')).toBe('page');
    expect((fixture.nativeElement as HTMLElement).querySelector('nav')?.getAttribute('aria-label')).toBe(
      arabicTranslations.shell.nav.label,
    );
  });

  it('signed in, the shell has a Clinics entry next to Specialties that marks the current page', async () => {
    const fixture = await render('/');
    await TestBed.inject(Router).navigateByUrl('/clinics');
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/clinics').flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('nav a')];
    expect(links.map((l) => l.getAttribute('href'))).toEqual(['/specialties', '/clinics', '/doctors']);
    expect(links[1].textContent?.trim()).toBe(arabicTranslations.shell.nav.clinics);
    expect(links[1].getAttribute('aria-current')).toBe('page');
    expect(links[0].hasAttribute('aria-current')).toBe(false);
  });

  // jsdom does no layout, so this cannot prove that the header fits at 360 px: it only guards the classes
  // that make it wrap and shorten the user name. The real check is by hand at phone width, in RTL and LTR (D56).
  it('the header keeps its wrapping classes so the title, two links, user name, sign-out and language switcher can wrap', async () => {
    const fixture = await render('/');
    const root = fixture.nativeElement as HTMLElement;

    const header = root.querySelector('header')!;
    expect(header.className).toContain('flex-wrap');
    const groups = [...header.querySelectorAll(':scope > div')];
    expect(groups).toHaveLength(2);
    expect(groups.every((g) => g.className.includes('flex-wrap') && g.className.includes('min-w-0'))).toBe(true);
    expect(root.querySelector('nav ul')!.className).toContain('flex-wrap');
    expect(root.querySelector('header strong')!.className).toContain('truncate');
    expect(header.className).not.toMatch(/\bnowrap\b|whitespace-nowrap|overflow-x/);
  });

  it('signed out, there is no navigation', async () => {
    const fixture = await render('/login', false);

    expect((fixture.nativeElement as HTMLElement).querySelector('nav')).toBeNull();
  });

  it('signed out, the shell has no user name or sign-out button but keeps the language switcher', async () => {
    const fixture = await render('/login', false);

    expect(text(fixture, 'header strong')).toBeUndefined();
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('header button').length).toBe(1);
    expect(text(fixture, 'cb-language-switcher button')).toBe(arabicTranslations.language.en);
  });

  it('signed out, a deep link goes to the login page and comes back after signing in', async () => {
    const fixture = await render('/specialties', false);

    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fspecialties');
    expect(text(fixture, 'h2')).toBe(arabicTranslations.login.title);
  });

  it('shows the sign-in page, not the shell, when the visitor has no session (silent restore fails)', async () => {
    const restore = TestBed.inject(SessionService).restore();
    http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
    await restore;

    const fixture = await render('/', false);

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(text(fixture, 'main p')).toBeUndefined();
  });

  it('keeps the user signed in across a reload: silent restore shows the shell without the login page', async () => {
    const restore = TestBed.inject(SessionService).restore();
    http.expectOne('/api/auth/refresh').flush(tokenResponse('restored'));
    http.expectOne('/api/auth/me').flush({ id: 1, userName: 'back', permissions: [] });
    await restore;

    const fixture = await render('/', false);

    expect(TestBed.inject(Router).url).toBe('/');
    expect(text(fixture, 'header strong')).toBe('back');
  });

  it('sign out calls the API, clears the session and returns to the login page', async () => {
    const fixture = await render();
    const signOut = (fixture.nativeElement as HTMLElement).querySelector('header div button') as HTMLButtonElement;
    expect(signOut.textContent?.trim()).toBe(arabicTranslations.shell.sign_out);

    signOut.click();
    http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(TestBed.inject(SessionService).state()).toBe('anonymous');
    expect(text(fixture, 'header strong')).toBeUndefined();
  });
});
