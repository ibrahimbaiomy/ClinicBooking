import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideAuthTesting } from '../../../testing/auth-testing';
import { usersArabic as uar, usersEnglish as uen } from '../../../testing/transloco-testing';
import { LanguageService } from '../../core/i18n/language.service';
import { PermissionLabels } from './permission-labels';

describe('PermissionLabels (D59)', () => {
  let labels: PermissionLabels;
  let transloco: TranslocoService;

  beforeEach(async () => {
    localStorage.setItem('clinicbooking.lang', 'ar');
    TestBed.configureTestingModule({ providers: provideAuthTesting() });
    transloco = TestBed.inject(TranslocoService);
    await TestBed.inject(LanguageService).load();
    await new Promise<void>((resolve) => transloco.load('users/ar').subscribe(() => resolve()));
    labels = TestBed.inject(PermissionLabels);
  });

  it('reads the label and description of a dotted permission name from the nested object', () => {
    expect(labels.labelOf('users.manage')).toEqual({
      label: uar.permissions.users.manage.label,
      description: uar.permissions.users.manage.description,
    });
    expect(labels.labelOf('doctors.manage')?.label).toBe(uar.permissions.doctors.manage.label);
  });

  it('has a label for every permission the UI knows (the check:permissions script proves the C# side)', () => {
    for (const name of ['users.manage', 'specialties.manage', 'clinics.manage', 'doctors.manage']) {
      expect(labels.labelOf(name), name).not.toBeNull();
    }
  });

  it('returns null for a name without a label, so the caller shows the bare name', () => {
    expect(labels.labelOf('reports.view')).toBeNull();
    expect(labels.labelOf('users')).toBeNull(); // a group, not a permission
    expect(labels.labelOf('users.manage.extra')).toBeNull();
    expect(labels.labelOf('')).toBeNull();
  });

  it('follows the language', async () => {
    await new Promise<void>((resolve) => transloco.load('users/en').subscribe(() => resolve()));

    TestBed.inject(LanguageService).set('en');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(labels.labelOf('users.manage')?.label).toBe(uen.permissions.users.manage.label);
  });
});
