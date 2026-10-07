/** Mirror of DoctorSlotDuration: 5 to 120 minutes in steps of 5 (D43, D61). */
export const SLOT_MINUTES: readonly number[] = Array.from({ length: 24 }, (_, index) => (index + 1) * 5);

const CAIRO_DAY = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Africa/Cairo',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

/** Today's calendar date in Cairo as "YYYY-MM-DD" (the API's "today" for slot changes, D12, D61). */
export function cairoToday(now: Date = new Date()): string {
  return CAIRO_DAY.format(now);
}

/**
 * The first date a slot change may take effect: tomorrow in Cairo, "YYYY-MM-DD". The browser clock is
 * only a hint for the date picker; the API decides (422 error.doctor.effective_from_not_future).
 */
export function cairoTomorrow(now: Date = new Date()): string {
  const [year, month, day] = cairoToday(now).split('-').map(Number);
  const next = new Date(Date.UTC(year, month - 1, day + 1));
  return next.toISOString().slice(0, 10);
}
