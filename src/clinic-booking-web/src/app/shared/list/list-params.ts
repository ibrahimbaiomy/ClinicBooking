import { ParamMap, Params } from '@angular/router';

/**
 * The URL and API handling every list shares (D53, D62): search, page, page size, sort field and
 * direction. A feature adds its own filters and sort fields around these helpers and keeps its own
 * defaults and API mapping.
 */
export const SORT_DIRECTIONS = ['asc', 'desc'] as const;
export const PAGE_SIZES = [10, 20, 50] as const;

/** Mirror of the API's MaxSearchLength (100 on every list). */
export const MAX_SEARCH_LENGTH = 100;

export type SortDirection = (typeof SORT_DIRECTIONS)[number];

export interface PagedSortedState<F extends string> {
  search: string;
  page: number;
  pageSize: number;
  sortBy: F;
  sortDirection: SortDirection;
}

export const isOneOf = <T extends string | number>(list: readonly T[], value: unknown): value is T =>
  list.some((item) => item === value);

export function normalizeSearch(text: string | null | undefined): string {
  return (text ?? '').trim().slice(0, MAX_SEARCH_LENGTH);
}

/** Reads `q`, `page`, `size`, `sort` and `dir`. Anything invalid falls back to the default. */
export function parsePagedSorted<F extends string>(
  params: ParamMap,
  sortFields: readonly F[],
  defaults: PagedSortedState<F>,
): PagedSortedState<F> {
  const page = Number(params.get('page'));
  const size = Number(params.get('size'));
  const sort = params.get('sort');
  const dir = params.get('dir');

  return {
    search: normalizeSearch(params.get('q')),
    page: Number.isSafeInteger(page) && page >= 1 ? page : defaults.page,
    pageSize: isOneOf(PAGE_SIZES, size) ? size : defaults.pageSize,
    sortBy: isOneOf(sortFields, sort) ? sort : defaults.sortBy,
    sortDirection: isOneOf(SORT_DIRECTIONS, dir) ? dir : defaults.sortDirection,
  };
}

/** Writes the search into `q`; the URL omits the defaults, so the default list has a clean URL. */
export function searchParam(state: { search: string }): Params {
  return state.search === '' ? {} : { q: state.search };
}

/** Writes `page`, `size`, `sort` and `dir`, each only when it differs from the default. */
export function pagedSortedParams<F extends string>(state: PagedSortedState<F>, defaults: PagedSortedState<F>): Params {
  const params: Params = {};
  if (state.page !== defaults.page) params['page'] = state.page;
  if (state.pageSize !== defaults.pageSize) params['size'] = state.pageSize;
  if (state.sortBy !== defaults.sortBy) params['sort'] = state.sortBy;
  if (state.sortDirection !== defaults.sortDirection) params['dir'] = state.sortDirection;
  return params;
}

/** The API's PascalCase names: always explicit, except an empty search, which is omitted. */
export function pagedSortedQuery<F extends string>(
  state: PagedSortedState<F>,
): { Search?: string; Page: number; PageSize: number; SortBy: F; SortDirection: SortDirection } {
  return {
    ...(state.search === '' ? {} : { Search: state.search }),
    Page: state.page,
    PageSize: state.pageSize,
    SortBy: state.sortBy,
    SortDirection: state.sortDirection,
  };
}

export function isSamePagedSorted<F extends string>(a: PagedSortedState<F>, b: PagedSortedState<F>): boolean {
  return (
    a.search === b.search &&
    a.page === b.page &&
    a.pageSize === b.pageSize &&
    a.sortBy === b.sortBy &&
    a.sortDirection === b.sortDirection
  );
}
