import { Params, ParamMap } from '@angular/router';
import { SpecialtyListQuery } from '../../../../api/specialties-api';
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

/** Mirror of the API's allowed values (the schema types them as plain strings; ListSpecialtiesQuery). */
export const SORT_FIELDS = ['nameAr', 'nameEn', 'createdAt'] as const;

export type SortField = (typeof SORT_FIELDS)[number];

export type ListState = PagedSortedState<SortField>;

/** The one place the defaults live (Arabic-first UI: Arabic name ascending, D53). */
export const DEFAULT_LIST_STATE: ListState = {
  search: '',
  page: 1,
  pageSize: 20,
  sortBy: 'nameAr',
  sortDirection: 'asc',
};

/** Reads the URL. Anything invalid falls back to the default, so a bad link never breaks the page. */
export function parseListState(params: ParamMap): ListState {
  return parsePagedSorted(params, SORT_FIELDS, DEFAULT_LIST_STATE);
}

/** The URL for a state: defaults are omitted, so the default list has a clean URL. */
export function toQueryParams(state: ListState): Params {
  return { ...searchParam(state), ...pagedSortedParams(state, DEFAULT_LIST_STATE) };
}

/** The API query for a state: always explicit, except an empty search, which is omitted. */
export function toApiQuery(state: ListState): SpecialtyListQuery {
  return pagedSortedQuery(state);
}

export function isSameListState(a: ListState, b: ListState): boolean {
  return isSamePagedSorted(a, b);
}
