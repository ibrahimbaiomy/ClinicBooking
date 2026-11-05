import { SortDirection } from './list-params';

/**
 * Sorting from a table header (D64). A column stands for one sort field, or for several when it shows
 * several values that each sort (the two names: `nameAr` and `nameEn`); `primary` is the field a first
 * click uses (for the names, the one in the UI language).
 */
export interface SortColumn<F extends string> {
  fields: readonly F[];
  primary: F;
}

/** A column that stands for one field. */
export const singleColumn = <F extends string>(field: F): SortColumn<F> => ({ fields: [field], primary: field });

/** The column that shows both names: sorted by either; a first click sorts by the UI-language name (D64). */
export const namesColumn = (language: 'ar' | 'en'): SortColumn<'nameAr' | 'nameEn'> => ({
  fields: ['nameAr', 'nameEn'],
  primary: language === 'ar' ? 'nameAr' : 'nameEn',
});

interface Sorted<F extends string> {
  sortBy: F;
  sortDirection: SortDirection;
}

/** The column is the current sort: a click toggles the direction. Another column starts ascending. */
export function nextSort<F extends string>(current: Sorted<F>, column: SortColumn<F>): Sorted<F> {
  if (column.fields.includes(current.sortBy)) {
    return { sortBy: current.sortBy, sortDirection: current.sortDirection === 'asc' ? 'desc' : 'asc' };
  }
  return { sortBy: column.primary, sortDirection: 'asc' };
}

/** The direction this column is sorted in, or null when the list is sorted by another column. */
export function columnDirection<F extends string>(current: Sorted<F>, column: SortColumn<F>): SortDirection | null {
  return column.fields.includes(current.sortBy) ? current.sortDirection : null;
}

/** The `aria-sort` value for the column's `th`: absent when it is not the sort. */
export function ariaSort(direction: SortDirection | null): 'ascending' | 'descending' | null {
  if (direction === null) return null;
  return direction === 'asc' ? 'ascending' : 'descending';
}
