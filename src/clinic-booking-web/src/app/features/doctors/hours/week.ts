/**
 * The working-hours week on screen (D61, D62). The API keeps .NET DayOfWeek (0 = Sunday ... 6 = Saturday);
 * the screen shows Saturday to Friday. Every mapping between the two lives here.
 */

/** The displayed week: Saturday first, as .NET DayOfWeek numbers. */
export const DISPLAY_DAYS: readonly number[] = [6, 0, 1, 2, 3, 4, 5];

/** Weekday names by .NET DayOfWeek. */
export const DAY_KEYS: Readonly<Record<number, string>> = {
  // i18n-keys: doctors.days.sunday, doctors.days.monday, doctors.days.tuesday, doctors.days.wednesday, doctors.days.thursday, doctors.days.friday, doctors.days.saturday
  0: 'doctors.days.sunday',
  1: 'doctors.days.monday',
  2: 'doctors.days.tuesday',
  3: 'doctors.days.wednesday',
  4: 'doctors.days.thursday',
  5: 'doctors.days.friday',
  6: 'doctors.days.saturday',
};

/** One period being edited. `key` identifies it on screen; times are "HH:mm" or '' while not entered. */
export interface EditablePeriod {
  key: number;
  start: string;
  end: string;
}

export interface DayView {
  dayOfWeek: number;
  periods: EditablePeriod[];
}

/** A period of the request that was sent: which day and which displayed period. */
export interface PeriodRef {
  dayOfWeek: number;
  key: number;
}

export interface WeekRequest {
  periods: { dayOfWeek: number; start: string; end: string }[];
  /** `refs[i]` is the displayed period behind `periods[i]` (field errors and periodIndex, D62). */
  refs: PeriodRef[];
}

/** "09:00:00" (as the API answers) or "09:00" to "09:00"; anything else unchanged. */
export function toInputTime(time: string): string {
  return /^\d{2}:\d{2}(:\d{2})?$/.test(time) ? time.slice(0, 5) : time;
}

/** The API's periods, grouped into the displayed week, each day ordered by start. Keys start at `firstKey`. */
export function toWeek(periods: readonly { dayOfWeek: number | string; start: string; end: string }[], firstKey = 1): DayView[] {
  let key = firstKey;
  return DISPLAY_DAYS.map((dayOfWeek) => ({
    dayOfWeek,
    periods: periods
      .filter((period) => Number(period.dayOfWeek) === dayOfWeek)
      .map((period) => ({ start: toInputTime(period.start), end: toInputTime(period.end) }))
      .sort((a, b) => a.start.localeCompare(b.start))
      .map((period) => ({ key: key++, ...period })),
  }));
}

/**
 * The request in the displayed order: Saturday first, then each day by start (a period without a start keeps
 * its place after the others). Times are sent as "HH:mm", which the API accepts (tested against it, D62).
 */
export function toRequest(week: readonly DayView[]): WeekRequest {
  const request: WeekRequest = { periods: [], refs: [] };
  for (const dayOfWeek of DISPLAY_DAYS) {
    const day = week.find((d) => d.dayOfWeek === dayOfWeek);
    const ordered = [...(day?.periods ?? [])].sort((a, b) => byStart(a.start, b.start));
    for (const period of ordered) {
      request.periods.push({ dayOfWeek, start: toInputTime(period.start), end: toInputTime(period.end) });
      request.refs.push({ dayOfWeek, key: period.key });
    }
  }
  return request;
}

/** The displayed period for a request index, or null when the index is out of range. */
export function periodAt(refs: readonly PeriodRef[], index: number | null): PeriodRef | null {
  return index !== null && Number.isInteger(index) && index >= 0 && index < refs.length ? refs[index] : null;
}

/** "periods[3].start" to { index: 3, field: 'start' }; any other name to null. */
export function parsePeriodField(name: string): { index: number; field: 'start' | 'end' | 'dayOfWeek' } | null {
  const match = /^periods\[(\d+)\]\.(start|end|dayOfWeek)$/.exec(name);
  return match === null ? null : { index: Number(match[1]), field: match[2] as 'start' | 'end' | 'dayOfWeek' };
}

function byStart(a: string, b: string): number {
  if (a === '' || b === '') return a === b ? 0 : a === '' ? 1 : -1;
  return a.localeCompare(b);
}
