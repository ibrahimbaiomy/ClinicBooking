import { Params, ParamMap } from '@angular/router';
import { SpecialtyListQuery } from '../../../../api/specialties-api';

/** Mirror of the API's allowed values (the schema types them as plain strings). */
export const SORT_FIELDS = ['nameAr', 'nameEn', 'createdAt'] as const;
export const SORT_DIRECTIONS = ['asc', 'desc'] as const;
export const PAGE_SIZES = [10, 20, 50] as const;

/** Mirror of ListSpecialtiesQuery.MaxSearchLength. */
export const MAX_SEARCH_LENGTH = 100;

export type SortField = (typeof SORT_FIELDS)[number];
export type SortDirection = (typeof SORT_DIRECTIONS)[number];

export interface ListState {
  search: string;
  page: number;
  pageSize: number;
  sortBy: SortField;
  sortDirection: SortDirection;
}

/** The one place the defaults live (Arabic-first UI: Arabic name ascending, D53). */
export const DEFAULT_LIST_STATE: ListState = {
  search: '',
  page: 1,
  pageSize: 20,
  sortBy: 'nameAr',
  sortDirection: 'asc',
};

const isOneOf = <T extends string | number>(list: readonly T[], value: unknown): value is T =>
  list.some((item) => item === value);

export function normalizeSearch(text: string | null | undefined): string {
  return (text ?? '').trim().slice(0, MAX_SEARCH_LENGTH);
}

/** Reads the URL. Anything invalid falls back to the default, so a bad link never breaks the page. */
export function parseListState(params: ParamMap): ListState {
  const defaults = DEFAULT_LIST_STATE;
  const page = Number(params.get('page'));
  const size = Number(params.get('size'));
  const sort = params.get('sort');
  const dir = params.get('dir');

  return {
    search: normalizeSearch(params.get('q')),
    page: Number.isSafeInteger(page) && page >= 1 ? page : defaults.page,
    pageSize: isOneOf(PAGE_SIZES, size) ? size : defaults.pageSize,
    sortBy: isOneOf(SORT_FIELDS, sort) ? sort : defaults.sortBy,
    sortDirection: isOneOf(SORT_DIRECTIONS, dir) ? dir : defaults.sortDirection,
  };
}

/** The URL for a state: defaults are omitted, so the default list has a clean URL. */
export function toQueryParams(state: ListState): Params {
  const defaults = DEFAULT_LIST_STATE;
  const params: Params = {};
  if (state.search !== '') params['q'] = state.search;
  if (state.page !== defaults.page) params['page'] = state.page;
  if (state.pageSize !== defaults.pageSize) params['size'] = state.pageSize;
  if (state.sortBy !== defaults.sortBy) params['sort'] = state.sortBy;
  if (state.sortDirection !== defaults.sortDirection) params['dir'] = state.sortDirection;
  return params;
}

/** The API query for a state: always explicit, except an empty search, which is omitted. */
export function toApiQuery(state: ListState): SpecialtyListQuery {
  return {
    ...(state.search === '' ? {} : { Search: state.search }),
    Page: state.page,
    PageSize: state.pageSize,
    SortBy: state.sortBy,
    SortDirection: state.sortDirection,
  };
}

export function isSameListState(a: ListState, b: ListState): boolean {
  return (
    a.search === b.search &&
    a.page === b.page &&
    a.pageSize === b.pageSize &&
    a.sortBy === b.sortBy &&
    a.sortDirection === b.sortDirection
  );
}
