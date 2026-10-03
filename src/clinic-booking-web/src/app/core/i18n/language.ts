export const LANGUAGES = ['ar', 'en'] as const;

export type Language = (typeof LANGUAGES)[number];

export type Direction = 'rtl' | 'ltr';

/** Arabic is the default language (D26). */
export const DEFAULT_LANGUAGE: Language = 'ar';

/** Also read by the inline script in index.html: keep the two in sync. */
export const LANGUAGE_STORAGE_KEY = 'clinicbooking.lang';

export function isLanguage(value: unknown): value is Language {
  return LANGUAGES.some((language) => language === value);
}

export function directionOf(language: Language): Direction {
  return language === 'ar' ? 'rtl' : 'ltr';
}
