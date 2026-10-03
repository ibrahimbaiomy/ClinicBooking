import { Injectable } from '@angular/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';
import { of } from 'rxjs';
import ar from '../../public/i18n/ar.json';
import en from '../../public/i18n/en.json';
import { DEFAULT_LANGUAGE, LANGUAGES } from '../app/core/i18n/language';

const TRANSLATIONS: Record<string, Translation> = { ar, en };

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

export { ar as arabicTranslations, en as englishTranslations };
