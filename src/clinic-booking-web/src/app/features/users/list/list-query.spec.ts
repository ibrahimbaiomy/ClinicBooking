import { convertToParamMap } from '@angular/router';
import {
  DEFAULT_LIST_STATE,
  isSameListState,
  MAX_SEARCH_LENGTH,
  normalizeSearch,
  parseListState,
  toApiQuery,
  toQueryParams,
} from './list-query';

const parse = (params: Record<string, string>) => parseListState(convertToParamMap(params));

describe('users list query', () => {
  it('pins the defaults in one place: user name ascending, every status, page 1, 20 per page', () => {
    expect(DEFAULT_LIST_STATE).toEqual({
      search: '',
      status: 'all',
      page: 1,
      pageSize: 20,
      sortBy: 'userName',
      sortDirection: 'asc',
    });
  });

  it('the default list has a clean URL and an explicit API query without a status', () => {
    expect(toQueryParams(DEFAULT_LIST_STATE)).toEqual({});
    expect(parse({})).toEqual(DEFAULT_LIST_STATE);
    expect(toApiQuery(DEFAULT_LIST_STATE)).toEqual({ Page: 1, PageSize: 20, SortBy: 'userName', SortDirection: 'asc' });
  });

  it('reads every parameter from the URL', () => {
    expect(parse({ q: 'ann', status: 'disabled', page: '3', size: '50', sort: 'createdAt', dir: 'desc' })).toEqual({
      search: 'ann',
      status: 'disabled',
      page: 3,
      pageSize: 50,
      sortBy: 'createdAt',
      sortDirection: 'desc',
    });
  });

  it('falls back to the default for anything invalid', () => {
    expect(parse({ page: '0', size: '7', status: 'maybe', sort: 'passwordHash', dir: 'up' })).toEqual(DEFAULT_LIST_STATE);
    expect(parse({ page: 'abc' }).page).toBe(1);
    expect(parse({ page: '1.5' }).page).toBe(1);
  });

  it('writes only what differs from the defaults, and round-trips', () => {
    const state = { search: 'ann', status: 'active', page: 2, pageSize: 10, sortBy: 'createdAt', sortDirection: 'desc' } as const;

    const params = toQueryParams(state);

    expect(params).toEqual({ q: 'ann', status: 'active', page: 2, size: 10, sort: 'createdAt', dir: 'desc' });
    expect(parse(Object.fromEntries(Object.entries(params).map(([k, v]) => [k, String(v)])))).toEqual(state);
  });

  it('maps the status filter to IsActive: all omits it, active is true, disabled is false', () => {
    expect('IsActive' in toApiQuery({ ...DEFAULT_LIST_STATE, status: 'all' })).toBe(false);
    expect(toApiQuery({ ...DEFAULT_LIST_STATE, status: 'active' }).IsActive).toBe(true);
    expect(toApiQuery({ ...DEFAULT_LIST_STATE, status: 'disabled' }).IsActive).toBe(false);
  });

  it('sends a search only when there is one', () => {
    expect(toApiQuery({ ...DEFAULT_LIST_STATE, search: 'ann' }).Search).toBe('ann');
    expect('Search' in toApiQuery(DEFAULT_LIST_STATE)).toBe(false);
  });

  it('trims the search and caps it at the API limit', () => {
    expect(normalizeSearch('  ann  ')).toBe('ann');
    expect(normalizeSearch('x'.repeat(MAX_SEARCH_LENGTH + 20))).toHaveLength(MAX_SEARCH_LENGTH);
    expect(normalizeSearch(null)).toBe('');
  });

  it('compares states field by field, the status included', () => {
    expect(isSameListState(DEFAULT_LIST_STATE, { ...DEFAULT_LIST_STATE })).toBe(true);
    expect(isSameListState(DEFAULT_LIST_STATE, { ...DEFAULT_LIST_STATE, status: 'active' })).toBe(false);
  });
});
