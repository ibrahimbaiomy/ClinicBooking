import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { provideTestTransloco, arabicTranslations, englishTranslations } from '../testing/transloco-testing';
import { App } from './app';
import { routes } from './app.routes';
import { LANGUAGE_STORAGE_KEY } from './core/i18n/language';
import { LanguageService } from './core/i18n/language.service';

async function render(): Promise<ComponentFixture<App>> {
  const fixture = TestBed.createComponent(App);
  await TestBed.inject(LanguageService).load();
  await TestBed.inject(Router).navigateByUrl('/');
  await fixture.whenStable();
  return fixture;
}

const text = (fixture: ComponentFixture<App>, selector: string) =>
  (fixture.nativeElement as HTMLElement).querySelector(selector)?.textContent?.trim();

describe('App shell', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideTestTransloco()],
    });
  });

  it('runs without zone.js', () => {
    expect('Zone' in globalThis).toBe(false);
  });

  it('shows the Arabic shell by default: title, switcher and placeholder, right to left', async () => {
    const fixture = await render();

    expect(text(fixture, 'h1')).toBe(arabicTranslations.app.title);
    expect(text(fixture, 'button')).toBe(arabicTranslations.language.en);
    expect(text(fixture, 'main p')).toBe(arabicTranslations.shell.placeholder);
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
    expect(TestBed.inject(Title).getTitle()).toBe(arabicTranslations.app.title);
  });

  it('switching the language updates the UI, lang, dir and storage without zone.js', async () => {
    const fixture = await render();
    const button = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;

    button.click();
    await fixture.whenStable();

    expect(text(fixture, 'h1')).toBe(englishTranslations.app.title);
    expect(text(fixture, 'button')).toBe(englishTranslations.language.ar);
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

  it('shows the shell for an unknown deep link (the API serves index.html for it)', async () => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(LanguageService).load();
    await TestBed.inject(Router).navigateByUrl('/specialties');
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/');
    expect(text(fixture, 'main p')).toBe(arabicTranslations.shell.placeholder);
  });
});
