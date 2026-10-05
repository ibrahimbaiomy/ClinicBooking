import { Params, ParamMap } from '@angular/router';
import { UserListQuery } from '../../../../api/users-api';

/** Mirror of the API's allowed values (the schema types them as plain strings). */
export const SORT_FIELDS = ['userName', 'createdAt'] as const;
export const SORT_DIRECTIONS = ['asc', 'desc'] as const;
export const STATUS_FILTERS = ['all', 'active', 'disabled'] as const;
export const PAGE_SIZES = [10, 20, 50] as const;

/** Mirror of ListUsersQuery.MaxSearchLength. */
export const MAX_SEARCH_LENGTH = 100;

export type SortField = (typeof SORT_FIELDS)[number];
export type SortDirection = (typeof SORT_DIRECTIONS)[number];
export type StatusFilter = (typeof STATUS_FILTERS)[number];

export interface ListState {
  search: string;
  status: StatusFilter;
  page: number;
  pageSize: number;
  sortBy: SortField;
  sortDirection: SortDirection;
}

/** The one place the defaults live: user name ascending, every status, page 1, 20 per page (D53, D59). */
export const DEFAULT_LIST_STATE: ListState = {
  search: '',
  status: 'all',
  page: 1,
  pageSize: 20,
  sortBy: 'userName',
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
  const status = params.get('status');
  const sort = params.get('sort');
  const dir = params.get('dir');

  return {
    search: normalizeSearch(params.get('q')),
    status: isOneOf(STATUS_FILTERS, status) ? status : defaults.status,
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
  if (state.status !== defaults.status) params['status'] = state.status;
  if (state.page !== defaults.page) params['page'] = state.page;
  if (state.pageSize !== defaults.pageSize) params['size'] = state.pageSize;
  if (state.sortBy !== defaults.sortBy) params['sort'] = state.sortBy;
  if (state.sortDirection !== defaults.sortDirection) params['dir'] = state.sortDirection;
  return params;
}

/** The API query for a state: always explicit, except an empty search and "all", which are omitted. */
export function toApiQuery(state: ListState): UserListQuery {
  return {
    ...(state.search === '' ? {} : { Search: state.search }),
    ...(state.status === 'all' ? {} : { IsActive: state.status === 'active' }),
    Page: state.page,
    PageSize: state.pageSize,
    SortBy: state.sortBy,
    SortDirection: state.sortDirection,
  };
}

export function isSameListState(a: ListState, b: ListState): boolean {
  return (
    a.search === b.search &&
    a.status === b.status &&
    a.page === b.page &&
    a.pageSize === b.pageSize &&
    a.sortBy === b.sortBy &&
    a.sortDirection === b.sortDirection
  );
}
