import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ClinicsApi } from '../../../api/clinics-api';
import { SpecialtiesApi } from '../../../api/specialties-api';
import { Language } from '../../core/i18n/language';

export interface NameView {
  text: string;
  lang: 'ar' | 'en';
  dir: 'rtl' | 'ltr';
}

export const AR = (text: string): NameView => ({ text, lang: 'ar', dir: 'rtl' });
export const EN = (text: string): NameView => ({ text, lang: 'en', dir: 'ltr' });

/** The name in the UI language first and prominent, the other second (D53). */
export function namesFor(nameAr: string, nameEn: string, language: Language): { primary: NameView; secondary: NameView } {
  return language === 'ar'
    ? { primary: AR(nameAr), secondary: EN(nameEn) }
    : { primary: EN(nameEn), secondary: AR(nameAr) };
}

/** The name in the UI language, for labels such as "Delete {{ name }}". */
export const uiName = (named: { nameAr: string; nameEn: string }, language: Language): string =>
  language === 'ar' ? named.nameAr : named.nameEn;

const collators: Partial<Record<Language, Intl.Collator>> = {};

/**
 * Sorted by the name in the UI language (the API sorts nested lists by English, D61). Display order only:
 * the collator does not fold hamza forms the way the server's search does (D62).
 */
export function byUiName<T extends { nameAr: string; nameEn: string }>(items: readonly T[], language: Language): T[] {
  const collator = (collators[language] ??= new Intl.Collator(language, { sensitivity: 'base', numeric: true }));
  return [...items].sort((a, b) => collator.compare(uiName(a, language), uiName(b, language)));
}

/** A specialty or clinic offered in a select or a checkbox group. */
export interface ReferenceOption {
  id: string;
  nameAr: string;
  nameEn: string;
}

/** Loaded once, never paged: the first page of 100 (D62). `incomplete` says the API holds more. */
export interface ReferenceList {
  options: ReferenceOption[];
  incomplete: boolean;
}

/** The API's largest page; a longer list shows an "incomplete" note (a searchable picker is Later). */
export const REFERENCE_PAGE_SIZE = 100;

/** Specialties and clinics for the doctors' filters and forms. */
@Injectable({ providedIn: 'root' })
export class DoctorReferenceData {
  private readonly specialties = inject(SpecialtiesApi);
  private readonly clinics = inject(ClinicsApi);

  specialtyList(): Observable<ReferenceList> {
    return this.specialties
      .list({ Page: 1, PageSize: REFERENCE_PAGE_SIZE, SortBy: 'nameAr', SortDirection: 'asc' })
      .pipe(map((page) => toList(page.items, Number(page.totalCount))));
  }

  clinicList(): Observable<ReferenceList> {
    return this.clinics
      .list({ Page: 1, PageSize: REFERENCE_PAGE_SIZE, SortBy: 'nameAr', SortDirection: 'asc' })
      .pipe(map((page) => toList(page.items, Number(page.totalCount))));
  }
}

function toList(items: readonly { id: number | string; nameAr: string; nameEn: string }[], total: number): ReferenceList {
  return {
    options: items.map((item) => ({ id: String(item.id), nameAr: item.nameAr, nameEn: item.nameEn })),
    incomplete: total > items.length,
  };
}
