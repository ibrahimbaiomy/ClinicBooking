import { INTL_LOCALES, formatIntl } from './intl';

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;

describe('formatIntl', () => {
  it('uses the explicit locales from D27', () => {
    expect(INTL_LOCALES.ar).toBe('ar-EG-u-nu-latn-ca-gregory');
    expect(INTL_LOCALES.en).toBe('en-GB-u-nu-latn-ca-gregory');
  });

  describe('dates', () => {
    it('formats a calendar date in English', () => {
      expect(formatIntl('2026-10-04', 'date', 'en')).toBe('4 October 2026');
    });

    it('formats a calendar date in Arabic with Latin digits and the Gregorian month name', () => {
      const text = formatIntl('2026-10-04', 'date', 'ar');

      expect(text).toContain('أكتوبر');
      expect(text).toContain('2026');
      expect(text).toContain('4');
      expect(text).not.toMatch(ARABIC_INDIC_DIGITS);
    });

    it('never shifts a calendar date, whatever the time zone', () => {
      expect(formatIntl('2026-01-01', 'date', 'en')).toBe('1 January 2026');
      expect(formatIntl('2026-12-31', 'date', 'en')).toBe('31 December 2026');
    });

    it('rejects an impossible calendar date', () => {
      expect(formatIntl('2026-02-30', 'date', 'en')).toBe('');
    });

    it('does not accept a date without a time as a time or date-time', () => {
      expect(formatIntl('2026-10-04', 'time', 'en')).toBe('');
      expect(formatIntl('2026-10-04', 'datetime', 'en')).toBe('');
    });
  });

  describe('instants are shown in Cairo time (D12)', () => {
    it('uses UTC+2 in winter', () => {
      // 22:30 UTC on 15 January is 00:30 on 16 January in Cairo.
      expect(formatIntl('2026-01-15T22:30:00Z', 'time', 'en')).toBe('00:30');
      expect(formatIntl('2026-01-15T22:30:00Z', 'date', 'en')).toBe('16 January 2026');
    });

    it('uses UTC+3 in summer (daylight saving)', () => {
      // 22:30 UTC on 15 July is 01:30 on 16 July in Cairo.
      expect(formatIntl('2026-07-15T22:30:00Z', 'time', 'en')).toBe('01:30');
      expect(formatIntl('2026-07-15T22:30:00Z', 'date', 'en')).toBe('16 July 2026');
    });

    it('honours an offset in the input', () => {
      expect(formatIntl('2026-01-15T10:00:00+02:00', 'time', 'en')).toBe('10:00');
    });

    it('accepts a Date and an epoch in milliseconds', () => {
      const instant = Date.UTC(2026, 0, 15, 22, 30);

      expect(formatIntl(new Date(instant), 'time', 'en')).toBe('00:30');
      expect(formatIntl(instant, 'time', 'en')).toBe('00:30');
    });

    it('formats a date-time with both parts', () => {
      const text = formatIntl('2026-01-15T22:30:00Z', 'datetime', 'en');

      expect(text).toContain('16 January 2026');
      expect(text).toContain('00:30');
    });

    it('keeps Latin digits in Arabic times', () => {
      const text = formatIntl('2026-01-15T22:30:00Z', 'time', 'ar');

      expect(text).toMatch(/12:30|00:30/);
      expect(text).not.toMatch(ARABIC_INDIC_DIGITS);
    });

    it('lets the caller choose another time zone', () => {
      expect(formatIntl('2026-01-15T22:30:00Z', 'time', 'en', { timeZone: 'UTC' })).toBe('22:30');
    });
  });

  describe('numbers', () => {
    it('formats with Latin digits in both languages', () => {
      expect(formatIntl(1234567.891, 'number', 'en', { maximumFractionDigits: 2 })).toBe('1,234,567.89');

      const arabic = formatIntl(1234567.891, 'number', 'ar', { maximumFractionDigits: 2 });
      expect(arabic).toContain('1');
      expect(arabic).toContain('567');
      expect(arabic).not.toMatch(ARABIC_INDIC_DIGITS);
    });

    it('formats percentages', () => {
      expect(formatIntl(0.256, 'percent', 'en')).toBe('26%');
      expect(formatIntl(0.256, 'percent', 'ar')).not.toMatch(ARABIC_INDIC_DIGITS);
      expect(formatIntl(0.256, 'percent', 'ar')).toContain('26');
    });
  });

  describe('values that cannot be shown', () => {
    it.each([null, undefined, '', 'not a date', NaN, Infinity, {}, [], true])(
      'returns an empty string for %j',
      (value) => {
        expect(formatIntl(value, 'date', 'en')).toBe('');
        expect(formatIntl(value, 'number', 'en')).toBe('');
      },
    );

    it('does not treat a numeric string as a number', () => {
      expect(formatIntl('12', 'number', 'en')).toBe('');
    });

    it('does not treat a number as a calendar date string', () => {
      expect(formatIntl(Number.NaN, 'date', 'en')).toBe('');
    });
  });
});
