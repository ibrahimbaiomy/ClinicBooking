import { Params, ParamMap } from '@angular/router';
import { UserListQuery } from '../../../../api/users-api';
import {
  isOneOf,
  isSamePagedSorted,
  PagedSortedState,
  pagedSortedParams,
  pagedSortedQuery,
  parsePagedSorted,
  searchParam,
  SortDirection,
} from '../../../shared/list/list-params';

export { MAX_SEARCH_LENGTH, normalizeSearch, PAGE_SIZES, SORT_DIRECTIONS } from '../../../shared/list/list-params';
export type { SortDirection };

/** Mirror of the API's allowed values (the schema types them as plain strings; ListUsersQuery). */
export const SORT_FIELDS = ['userName', 'createdAt'] as const;
export const STATUS_FILTERS = ['all', 'active', 'disabled'] as const;

export type SortField = (typeof SORT_FIELDS)[number];
export type StatusFilter = (typeof STATUS_FILTERS)[number];

export interface ListState extends PagedSortedState<SortField> {
  status: StatusFilter;
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

/** Reads the URL. Anything invalid falls back to the default, so a bad link never breaks the page. */
export function parseListState(params: ParamMap): ListState {
  const status = params.get('status');
  return {
    ...parsePagedSorted(params, SORT_FIELDS, DEFAULT_LIST_STATE),
    status: isOneOf(STATUS_FILTERS, status) ? status : DEFAULT_LIST_STATE.status,
  };
}

/** The URL for a state: defaults are omitted, so the default list has a clean URL. */
export function toQueryParams(state: ListState): Params {
  return {
    ...searchParam(state),
    ...(state.status === DEFAULT_LIST_STATE.status ? {} : { status: state.status }),
    ...pagedSortedParams(state, DEFAULT_LIST_STATE),
  };
}

/** The API query for a state: always explicit, except an empty search and "all", which are omitted. */
export function toApiQuery(state: ListState): UserListQuery {
  const { Search, ...rest } = pagedSortedQuery(state);
  return {
    ...(Search === undefined ? {} : { Search }),
    ...(state.status === 'all' ? {} : { IsActive: state.status === 'active' }),
    ...rest,
  };
}

export function isSameListState(a: ListState, b: ListState): boolean {
  return a.status === b.status && isSamePagedSorted(a, b);
}
