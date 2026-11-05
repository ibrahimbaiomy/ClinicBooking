import { ariaSort, columnDirection, namesColumn, nextSort, singleColumn } from './header-sort';

type Field = 'nameAr' | 'nameEn' | 'phone' | 'createdAt';

describe('header sorting (D64)', () => {
  const phone = singleColumn<Field>('phone');

  it('a new column starts ascending', () => {
    expect(nextSort<Field>({ sortBy: 'createdAt', sortDirection: 'desc' }, phone)).toEqual({ sortBy: 'phone', sortDirection: 'asc' });
  });

  it('the same column toggles the direction', () => {
    expect(nextSort<Field>({ sortBy: 'phone', sortDirection: 'asc' }, phone)).toEqual({ sortBy: 'phone', sortDirection: 'desc' });
    expect(nextSort<Field>({ sortBy: 'phone', sortDirection: 'desc' }, phone)).toEqual({ sortBy: 'phone', sortDirection: 'asc' });
  });

  it('the names column sorts by the UI-language name first, and toggles while either name is the sort', () => {
    expect(nextSort<Field>({ sortBy: 'phone', sortDirection: 'asc' }, namesColumn('ar'))).toEqual({ sortBy: 'nameAr', sortDirection: 'asc' });
    expect(nextSort<Field>({ sortBy: 'phone', sortDirection: 'asc' }, namesColumn('en'))).toEqual({ sortBy: 'nameEn', sortDirection: 'asc' });
    // Sorted by the English name in the Arabic UI: the column is the sort, so it only toggles.
    expect(nextSort<Field>({ sortBy: 'nameEn', sortDirection: 'asc' }, namesColumn('ar'))).toEqual({ sortBy: 'nameEn', sortDirection: 'desc' });
  });

  it('reports the direction and aria-sort only for the sorted column', () => {
    const current = { sortBy: 'nameEn' as Field, sortDirection: 'desc' as const };
    expect(columnDirection(current, namesColumn('ar'))).toBe('desc');
    expect(columnDirection(current, phone)).toBeNull();
    expect(ariaSort('asc')).toBe('ascending');
    expect(ariaSort('desc')).toBe('descending');
    expect(ariaSort(null)).toBeNull();
  });
});
