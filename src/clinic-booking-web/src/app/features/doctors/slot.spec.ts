import { cairoToday, cairoTomorrow, SLOT_MINUTES } from './slot';

describe('slot helpers (D62)', () => {
  it('offers 5 to 120 minutes in steps of 5', () => {
    expect(SLOT_MINUTES[0]).toBe(5);
    expect(SLOT_MINUTES.at(-1)).toBe(120);
    expect(SLOT_MINUTES).toHaveLength(24);
    expect(SLOT_MINUTES.every((m) => m % 5 === 0)).toBe(true);
  });

  it('today and tomorrow are Cairo dates, not UTC ones', () => {
    // 22:30 UTC on 1 July 2026 is 01:30 on 2 July in Cairo (summer time).
    const lateUtc = new Date('2026-07-01T22:30:00Z');

    expect(cairoToday(lateUtc)).toBe('2026-07-02');
    expect(cairoTomorrow(lateUtc)).toBe('2026-07-03');
  });

  it('tomorrow crosses month and year ends', () => {
    expect(cairoTomorrow(new Date('2026-12-31T10:00:00Z'))).toBe('2027-01-01');
    expect(cairoTomorrow(new Date('2026-02-28T10:00:00Z'))).toBe('2026-03-01');
  });
});
