import { Params, ParamMap } from '@angular/router';
import { DoctorListQuery } from '../../../../api/doctors-api';
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

/** Mirror of the API's allowed values (the schema types them as plain strings; ListDoctorsQuery). */
export const SORT_FIELDS = ['nameAr', 'nameEn', 'createdAt'] as const;

/** `active` in the URL; it means something only with a clinic (error.doctor.is_active_requires_clinic, D61). */
export const STATUS_FILTERS = ['all', 'active', 'inactive'] as const;

export type SortField = (typeof SORT_FIELDS)[number];
export type StatusFilter = (typeof STATUS_FILTERS)[number];

export interface ListState extends PagedSortedState<SortField> {
  /** A specialty id, or '' for every specialty. */
  specialty: string;
  /** A clinic id, or '' for every clinic. */
  clinic: string;
  /** Always 'all' when no clinic is chosen. */
  status: StatusFilter;
}

/** The one place the defaults live: Arabic name ascending, no filter, page 1, 20 per page (D53, D62). */
export const DEFAULT_LIST_STATE: ListState = {
  search: '',
  specialty: '',
  clinic: '',
  status: 'all',
  page: 1,
  pageSize: 20,
  sortBy: 'nameAr',
  sortDirection: 'asc',
};

const id = (value: string | null): string => (value !== null && /^\d{1,18}$/.test(value) ? value : '');

/**
 * Reads the URL. Anything invalid falls back to the default, so a bad link never breaks the page; a
 * status without a clinic is dropped, so the list never sends what the API refuses.
 */
export function parseListState(params: ParamMap): ListState {
  const clinic = id(params.get('clinic'));
  const status = params.get('active');
  return {
    ...parsePagedSorted(params, SORT_FIELDS, DEFAULT_LIST_STATE),
    specialty: id(params.get('specialty')),
    clinic,
    status: clinic !== '' && isOneOf(STATUS_FILTERS, status) ? status : 'all',
  };
}

/** The URL for a state: defaults are omitted, so the default list has a clean URL. */
export function toQueryParams(state: ListState): Params {
  const withClinic = state.clinic !== '';
  return {
    ...searchParam(state),
    ...(state.specialty === '' ? {} : { specialty: state.specialty }),
    ...(withClinic ? { clinic: state.clinic } : {}),
    ...(withClinic && state.status !== 'all' ? { active: state.status } : {}),
    ...pagedSortedParams(state, DEFAULT_LIST_STATE),
  };
}

/** The API query: explicit paging and sort; filters only when set; IsActive only with a clinic. */
export function toApiQuery(state: ListState): DoctorListQuery {
  const { Search, ...rest } = pagedSortedQuery(state);
  return {
    ...(Search === undefined ? {} : { Search }),
    ...(state.specialty === '' ? {} : { SpecialtyId: state.specialty }),
    ...(state.clinic === '' ? {} : { ClinicId: state.clinic }),
    ...(state.clinic !== '' && state.status !== 'all' ? { IsActive: state.status === 'active' } : {}),
    ...rest,
  };
}

export function isSameListState(a: ListState, b: ListState): boolean {
  return a.specialty === b.specialty && a.clinic === b.clinic && a.status === b.status && isSamePagedSorted(a, b);
}
