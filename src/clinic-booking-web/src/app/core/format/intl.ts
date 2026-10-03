import { Language } from '../i18n/language';

export type IntlKind = 'date' | 'time' | 'datetime' | 'number' | 'percent';

export type IntlOptions = Intl.DateTimeFormatOptions & Intl.NumberFormatOptions;

/**
 * Latin digits and the Gregorian calendar in both languages (D27). Plain `ar-EG` would produce
 * Arabic-Indic digits. English uses `en-GB`: day first, 24-hour clock.
 */
export const INTL_LOCALES: Record<Language, string> = {
  ar: 'ar-EG-u-nu-latn-ca-gregory',
  en: 'en-GB-u-nu-latn-ca-gregory',
};

/** Instants are stored in UTC and shown in Cairo time (D12). */
export const DISPLAY_TIME_ZONE = 'Africa/Cairo';

const CALENDAR_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

const DATE_OPTIONS: Intl.DateTimeFormatOptions = { year: 'numeric', month: 'long', day: 'numeric' };
const TIME_OPTIONS: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit' };

const formatters = new Map<string, Intl.DateTimeFormat | Intl.NumberFormat>();

function cached<T extends Intl.DateTimeFormat | Intl.NumberFormat>(
  key: string,
  create: () => T,
): T {
  let formatter = formatters.get(key);
  if (!formatter) {
    formatter = create();
    formatters.set(key, formatter);
  }
  return formatter as T;
}

interface ParsedDate {
  date: Date;
  /** A date with no time (for example "2026-10-04"): a calendar day, never shifted by a time zone. */
  calendarDay: boolean;
}

function parseDate(value: unknown): ParsedDate | null {
  if (typeof value === 'string') {
    const calendar = CALENDAR_DATE.exec(value);
    if (calendar) {
      const [year, month, day] = [Number(calendar[1]), Number(calendar[2]), Number(calendar[3])];
      const date = new Date(Date.UTC(year, month - 1, day));
      const valid =
        date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
      return valid ? { date, calendarDay: true } : null;
    }
  }

  if (typeof value === 'string' || typeof value === 'number' || value instanceof Date) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : { date, calendarDay: false };
  }

  return null;
}

/**
 * Formats a value for display. Returns an empty string for null, undefined and anything that
 * cannot be formatted, so a template never shows "Invalid Date".
 *
 * Dates and times are shown in Cairo time unless `options.timeZone` says otherwise; a date-only
 * string is a calendar day and is only valid for the `date` kind.
 */
export function formatIntl(
  value: unknown,
  kind: IntlKind,
  language: Language,
  options: IntlOptions = {},
): string {
  const locale = INTL_LOCALES[language];

  if (kind === 'number' || kind === 'percent') {
    if (typeof value !== 'number' || !Number.isFinite(value)) {
      return '';
    }

    const key = `${locale}|${kind}|${JSON.stringify(options)}`;
    return cached(key, () => new Intl.NumberFormat(locale, { ...options, style: kind === 'percent' ? 'percent' : options.style })).format(value);
  }

  const parsed = parseDate(value);
  if (!parsed || (parsed.calendarDay && kind !== 'date')) {
    return '';
  }

  const defaults =
    kind === 'date' ? DATE_OPTIONS : kind === 'time' ? TIME_OPTIONS : { ...DATE_OPTIONS, ...TIME_OPTIONS };
  const timeZone = parsed.calendarDay ? 'UTC' : (options.timeZone ?? DISPLAY_TIME_ZONE);
  const merged: Intl.DateTimeFormatOptions = { ...defaults, ...options, timeZone };

  const key = `${locale}|${kind}|${JSON.stringify(merged)}`;
  return cached(key, () => new Intl.DateTimeFormat(locale, merged)).format(parsed.date);
}
