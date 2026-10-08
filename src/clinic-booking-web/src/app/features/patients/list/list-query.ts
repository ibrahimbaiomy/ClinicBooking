import { Params, ParamMap } from '@angular/router';
import { PatientListQuery } from '../../../../api/patients-api';
import {
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

/** Mirror of the API's allowed values (ListPatientsQuery, D63). */
export const SORT_FIELDS = ['name', 'createdAt'] as const;

export type SortField = (typeof SORT_FIELDS)[number];

export type ListState = PagedSortedState<SortField>;

/** The one place the defaults live: name ascending, page 1, 20 per page (D53, D63). */
export const DEFAULT_LIST_STATE: ListState = {
  search: '',
  page: 1,
  pageSize: 20,
  sortBy: 'name',
  sortDirection: 'asc',
};

export function parseListState(params: ParamMap): ListState {
  return parsePagedSorted(params, SORT_FIELDS, DEFAULT_LIST_STATE);
}

export function toQueryParams(state: ListState): Params {
  return { ...searchParam(state), ...pagedSortedParams(state, DEFAULT_LIST_STATE) };
}

/** The search is sent as typed (trimmed): the server reads it as a name or a phone (D63). */
export function toApiQuery(state: ListState): PatientListQuery {
  return pagedSortedQuery(state);
}

export function isSameListState(a: ListState, b: ListState): boolean {
  return isSamePagedSorted(a, b);
}
