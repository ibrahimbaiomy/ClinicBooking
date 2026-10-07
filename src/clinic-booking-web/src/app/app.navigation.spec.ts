import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideAuthTesting, signIn } from '../testing/auth-testing';
import { arabicTranslations as ar, englishTranslations as en } from '../testing/transloco-testing';
import { App } from './app';
import { routes } from './app.routes';
import { SessionService } from './core/auth/session.service';
import { LANGUAGE_STORAGE_KEY } from './core/i18n/language';
import { LanguageService } from './core/i18n/language.service';

async function render(
  permissions: string[],
  options: { forced?: boolean; url?: string; language?: 'ar' | 'en' } = {},
): Promise<ComponentFixture<App>> {
  localStorage.setItem(LANGUAGE_STORAGE_KEY, options.language ?? 'ar');
  await signIn(permissions, 'token-1', { mustChangePassword: options.forced ?? false });
  const fixture = TestBed.createComponent(App);
  await TestBed.inject(LanguageService).load();
  await TestBed.inject(Router).navigateByUrl(options.url ?? '/');
  await fixture.whenStable();
  return fixture;
}

const links = (fixture: ComponentFixture<App>) =>
  [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('header a')].map((a) => a.getAttribute('href'));

describe('header navigation (D59)', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting(routes) });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('every signed-in user sees Specialties, Clinics, Doctors and Change password, and no Users link', async () => {
    const fixture = await render([]);

    expect(links(fixture)).toEqual(['/specialties', '/clinics', '/doctors', '/change-password']);
  });

  it('a user with users.manage also sees the Users link, between Doctors and Change password', async () => {
    const fixture = await render(['users.manage']);

    expect(links(fixture)).toEqual(['/specialties', '/clinics', '/doctors', '/users', '/change-password']);
    const users = (fixture.nativeElement as HTMLElement).querySelector('header a[href="/users"]')!;
    expect(users.textContent?.trim()).toBe(ar.shell.nav.users);
  });

  it('the Doctors link follows Clinics for every signed-in user, with or without doctors.manage (D62)', async () => {
    const fixture = await render([]);

    const doctors = (fixture.nativeElement as HTMLElement).querySelector('header a[href="/doctors"]')!;
    expect(doctors.textContent?.trim()).toBe(ar.shell.nav.doctors);
  });

  it('other permissions do not show the Users link', async () => {
    const fixture = await render(['clinics.manage', 'specialties.manage']);

    expect(links(fixture)).not.toContain('/users');
  });

  it('a clinic-scoped users.manage does not show it either', async () => {
    await signIn([], 'token-1', {
      clinicPermissions: [{ clinicId: 1, clinicNameAr: 'أ', clinicNameEn: 'A', permissions: ['users.manage'] }],
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(links(fixture)).not.toContain('/users');
  });

  it('the Change password link is worded in the active language, and marks the current page', async () => {
    const fixture = await render([], { url: '/change-password', language: 'en' });

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('header a[href="/change-password"]')!;
    expect(link.textContent?.trim()).toBe(en.shell.nav.change_password);
    expect(link.getAttribute('aria-current')).toBe('page');
    expect(TestBed.inject(Router).url).toBe('/change-password');
  });

  describe('while a password change is required', () => {
    it('shows no navigation and no Change password link, only the title, the user, sign-out and the language switcher', async () => {
      const fixture = await render(['users.manage'], { forced: true });
      const element = fixture.nativeElement as HTMLElement;

      expect(TestBed.inject(Router).url).toBe('/change-password');
      expect(links(fixture)).toEqual([]);
      expect(element.querySelector('header nav')).toBeNull();
      expect(element.querySelector('header h1')!.textContent?.trim()).toBe(ar.app.title);
      expect(element.querySelector('header strong')!.textContent).toBe('someone');
      expect(element.querySelector('cb-language-switcher button')).not.toBeNull();
      const signOut = [...element.querySelectorAll<HTMLButtonElement>('header button')].find((b) => b.textContent?.trim() === ar.shell.sign_out);
      expect(signOut).toBeDefined();
    });

    it('the language switcher still works', async () => {
      const fixture = await render([], { forced: true });
      const element = fixture.nativeElement as HTMLElement;

      element.querySelector<HTMLButtonElement>('cb-language-switcher button')!.click();
      await fixture.whenStable();

      expect(element.querySelector('header h1')!.textContent?.trim()).toBe(en.app.title);
      expect(document.documentElement.dir).toBe('ltr');
    });

    it('sign-out works, and the next visitor sees the full navigation again once they have changed the password', async () => {
      const fixture = await render([], { forced: true });
      const element = fixture.nativeElement as HTMLElement;
      const signOut = [...element.querySelectorAll<HTMLButtonElement>('header button')].find((b) => b.textContent?.trim() === ar.shell.sign_out)!;

      signOut.click();
      http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
      await fixture.whenStable();

      expect(TestBed.inject(Router).url.startsWith('/login')).toBe(true);
      expect(TestBed.inject(SessionService).isAuthenticated()).toBe(false);
    });

    it('the navigation comes back as soon as the flag is cleared', async () => {
      const fixture = await render([], { forced: true });
      expect(links(fixture)).toEqual([]);

      TestBed.inject(SessionService).clearPasswordChangeRequired();
      await fixture.whenStable();

      expect(links(fixture)).toEqual(['/specialties', '/clinics', '/doctors', '/change-password']);
    });
  });
});
