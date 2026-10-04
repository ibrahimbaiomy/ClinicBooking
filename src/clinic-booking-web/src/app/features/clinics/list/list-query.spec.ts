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

describe('list query', () => {
  it('pins the defaults in one place: Arabic name ascending, page 1, 20 per page', () => {
    expect(DEFAULT_LIST_STATE).toEqual({ search: '', page: 1, pageSize: 20, sortBy: 'nameAr', sortDirection: 'asc' });
  });

  it('the default list has a clean URL', () => {
    expect(toQueryParams(DEFAULT_LIST_STATE)).toEqual({});
    expect(parse({})).toEqual(DEFAULT_LIST_STATE);
  });

  it('the default list sends an explicit API query (defaults are never left to the server)', () => {
    expect(toApiQuery(DEFAULT_LIST_STATE)).toEqual({
      Page: 1,
      PageSize: 20,
      SortBy: 'nameAr',
      SortDirection: 'asc',
    });
  });

  it('reads every parameter from the URL', () => {
    expect(parse({ q: 'قلب', page: '3', size: '50', sort: 'createdAt', dir: 'desc' })).toEqual({
      search: 'قلب',
      page: 3,
      pageSize: 50,
      sortBy: 'createdAt',
      sortDirection: 'desc',
    });
  });

  it.each([
    ['a negative page', { page: '-2' }],
    ['page zero', { page: '0' }],
    ['a fractional page', { page: '1.5' }],
    ['a non-numeric page', { page: 'abc' }],
    ['a page size that is not offered', { size: '7' }],
    ['a page size above the maximum', { size: '1000' }],
    ['an unknown sort field', { sort: 'password' }],
    ['an unknown direction', { dir: 'up' }],
  ])('falls back to the default for %s', (_name, params) => {
    expect(parse(params)).toEqual(DEFAULT_LIST_STATE);
  });

  it('writes only what differs from the defaults', () => {
    expect(toQueryParams({ ...DEFAULT_LIST_STATE, search: 'x', page: 2, sortDirection: 'desc' })).toEqual({
      q: 'x',
      page: 2,
      dir: 'desc',
    });
  });

  it('round-trips any state through the URL', () => {
    const state = { search: 'قلب وأوعية', page: 4, pageSize: 10, sortBy: 'nameEn', sortDirection: 'desc' } as const;

    const params = Object.fromEntries(Object.entries(toQueryParams(state)).map(([k, v]) => [k, String(v)]));

    expect(parse(params)).toEqual(state);
  });

  it('sends the search as typed, trimmed, and omits a blank one', () => {
    expect(toApiQuery({ ...DEFAULT_LIST_STATE, search: 'أحمد' }).Search).toBe('أحمد');
    expect('Search' in toApiQuery({ ...DEFAULT_LIST_STATE, search: '' })).toBe(false);
    expect(normalizeSearch('   ')).toBe('');
    expect(normalizeSearch('  cardio  ')).toBe('cardio');
    expect(normalizeSearch(null)).toBe('');
  });

  it('cuts a search longer than the API allows', () => {
    expect(normalizeSearch('a'.repeat(500))).toHaveLength(MAX_SEARCH_LENGTH);
  });

  it('compares states by value', () => {
    expect(isSameListState(DEFAULT_LIST_STATE, { ...DEFAULT_LIST_STATE })).toBe(true);
    expect(isSameListState(DEFAULT_LIST_STATE, { ...DEFAULT_LIST_STATE, page: 2 })).toBe(false);
  });
});
