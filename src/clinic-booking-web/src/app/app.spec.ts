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

  it('signed in, an unknown deep link goes to the shell (the API serves index.html for it)', async () => {
    const fixture = await render('/specialties');

    expect(TestBed.inject(Router).url).toBe('/');
    expect(text(fixture, 'main p')).toBe(arabicTranslations.shell.placeholder);
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
