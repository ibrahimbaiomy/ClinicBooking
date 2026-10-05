import { Injectable } from '@angular/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { of } from 'rxjs';
import ar from '../../public/i18n/ar.json';
import en from '../../public/i18n/en.json';
import clinicsAr from '../../public/i18n/clinics/ar.json';
import clinicsEn from '../../public/i18n/clinics/en.json';
import accountAr from '../../public/i18n/account/ar.json';
import accountEn from '../../public/i18n/account/en.json';
import usersAr from '../../public/i18n/users/ar.json';
import usersEn from '../../public/i18n/users/en.json';
import specialtiesAr from '../../public/i18n/specialties/ar.json';
import specialtiesEn from '../../public/i18n/specialties/en.json';
import { DEFAULT_LANGUAGE, LANGUAGES } from '../app/core/i18n/language';

const TRANSLATIONS: Record<string, Translation> = {
  ar,
  en,
  'specialties/ar': specialtiesAr,
  'specialties/en': specialtiesEn,
  'clinics/ar': clinicsAr,
  'clinics/en': clinicsEn,
  'account/ar': accountAr,
  'account/en': accountEn,
  'users/ar': usersAr,
  'users/en': usersEn,
};

/** Serves the real translation files from memory, so specs run against what ships. */
@Injectable()
class InMemoryLoader implements TranslocoLoader {
  getTranslation(language: string) {
    return of(TRANSLATIONS[language] ?? {});
  }
}

export function provideTestTransloco() {
  return provideTransloco({
    config: {
      availableLangs: [...LANGUAGES],
      defaultLang: DEFAULT_LANGUAGE,
      reRenderOnLangChange: true,
      prodMode: true,
    },
    loader: InMemoryLoader,
  });
}

export {
  ar as arabicTranslations,
  en as englishTranslations,
  specialtiesAr as specialtiesArabic,
  specialtiesEn as specialtiesEnglish,
  clinicsAr as clinicsArabic,
  clinicsEn as clinicsEnglish,
  accountAr as accountArabic,
  accountEn as accountEnglish,
  usersAr as usersArabic,
  usersEn as usersEnglish,
};
