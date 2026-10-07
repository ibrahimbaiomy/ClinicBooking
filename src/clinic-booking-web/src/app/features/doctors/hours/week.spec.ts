import { DAY_KEYS, DISPLAY_DAYS, parsePeriodField, periodAt, toInputTime, toRequest, toWeek } from './week';

describe('the working-hours week (D62)', () => {
  it('is shown from Saturday to Friday, as .NET DayOfWeek numbers', () => {
    expect(DISPLAY_DAYS).toEqual([6, 0, 1, 2, 3, 4, 5]);
    expect(DISPLAY_DAYS.map((day) => DAY_KEYS[day])).toEqual([
      'doctors.days.saturday',
      'doctors.days.sunday',
      'doctors.days.monday',
      'doctors.days.tuesday',
      'doctors.days.wednesday',
      'doctors.days.thursday',
      'doctors.days.friday',
    ]);
  });

  it('groups the API periods by displayed day, each day by start, with "HH:mm" times', () => {
    const week = toWeek([
      { dayOfWeek: 1, start: '17:00:00', end: '21:00:00' },
      { dayOfWeek: 6, start: '10:00:00', end: '12:00:00' },
      { dayOfWeek: 1, start: '09:00:00', end: '13:00:00' },
      { dayOfWeek: '5', start: '08:00:00', end: '09:00:00' },
    ]);

    expect(week.map((day) => day.dayOfWeek)).toEqual([6, 0, 1, 2, 3, 4, 5]);
    expect(week[0].periods).toEqual([{ key: 1, start: '10:00', end: '12:00' }]);
    expect(week[2].periods.map((p) => p.start)).toEqual(['09:00', '17:00']);
    expect(week[6].periods).toEqual([{ key: 4, start: '08:00', end: '09:00' }]);
  });

  it('builds the request Saturday first, then by start, and maps each index back to its displayed period', () => {
    const week = toWeek([]);
    week[2].periods = [
      { key: 30, start: '17:00', end: '21:00' },
      { key: 31, start: '09:00', end: '13:00' }, // added later, earlier in the day
    ];
    week[0].periods = [{ key: 10, start: '10:00', end: '12:00' }];
    week[6].periods = [{ key: 60, start: '', end: '' }];

    const request = toRequest(week);

    expect(request.periods).toEqual([
      { dayOfWeek: 6, start: '10:00', end: '12:00' },
      { dayOfWeek: 1, start: '09:00', end: '13:00' },
      { dayOfWeek: 1, start: '17:00', end: '21:00' },
      { dayOfWeek: 5, start: '', end: '' },
    ]);
    expect(request.refs).toEqual([
      { dayOfWeek: 6, key: 10 },
      { dayOfWeek: 1, key: 31 },
      { dayOfWeek: 1, key: 30 },
      { dayOfWeek: 5, key: 60 },
    ]);
    expect(periodAt(request.refs, 1)).toEqual({ dayOfWeek: 1, key: 31 });
  });

  it('an index outside the request names no period', () => {
    const refs = [{ dayOfWeek: 6, key: 1 }];

    expect(periodAt(refs, 0)).toEqual({ dayOfWeek: 6, key: 1 });
    expect(periodAt(refs, 1)).toBeNull();
    expect(periodAt(refs, -1)).toBeNull();
    expect(periodAt(refs, null)).toBeNull();
  });

  it('reads the API field names of period errors', () => {
    expect(parsePeriodField('periods[3].start')).toEqual({ index: 3, field: 'start' });
    expect(parsePeriodField('periods[0].end')).toEqual({ index: 0, field: 'end' });
    expect(parsePeriodField('periods[0].dayOfWeek')).toEqual({ index: 0, field: 'dayOfWeek' });
    expect(parsePeriodField('periods')).toBeNull();
    expect(parsePeriodField('rowVersion')).toBeNull();
  });

  it('times are sent as "HH:mm" whatever the input gave', () => {
    expect(toInputTime('09:05:00')).toBe('09:05');
    expect(toInputTime('09:05')).toBe('09:05');
    expect(toInputTime('')).toBe('');
  });
});
