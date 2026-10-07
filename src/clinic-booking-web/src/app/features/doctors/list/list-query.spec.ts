import { convertToParamMap } from '@angular/router';
import { DEFAULT_LIST_STATE, isSameListState, ListState, parseListState, toApiQuery, toQueryParams } from './list-query';

const parse = (params: Record<string, string>) => parseListState(convertToParamMap(params));

describe('doctors list-query (D62)', () => {
  it('an empty URL is the default state, and the default state is an empty URL', () => {
    expect(parse({})).toEqual(DEFAULT_LIST_STATE);
    expect(toQueryParams(DEFAULT_LIST_STATE)).toEqual({});
  });

  it('reads and writes every filter, the paging and the sort', () => {
    const params = { q: 'أحمد', specialty: '5', clinic: '3', active: 'inactive', page: '2', size: '50', sort: 'createdAt', dir: 'desc' };

    const state = parse(params);

    expect(state).toEqual({
      search: 'أحمد',
      specialty: '5',
      clinic: '3',
      status: 'inactive',
      page: 2,
      pageSize: 50,
      sortBy: 'createdAt',
      sortDirection: 'desc',
    });
    expect(toQueryParams(state)).toEqual({ ...params, page: 2, size: 50 });
  });

  it('drops a status without a clinic, so the API is never sent IsActive alone', () => {
    const state = parse({ active: 'active' });

    expect(state.status).toBe('all');
    expect(toQueryParams({ ...DEFAULT_LIST_STATE, status: 'active' })).toEqual({});
    expect(toApiQuery({ ...DEFAULT_LIST_STATE, status: 'inactive' })).not.toHaveProperty('IsActive');
  });

  it.each([
    [{ clinic: 'abc' }, { clinic: '' }],
    [{ specialty: '-1' }, { specialty: '' }],
    [{ clinic: '3', active: 'maybe' }, { clinic: '3', status: 'all' }],
    [{ sort: 'specialty' }, { sortBy: 'nameAr' }],
  ] as [Record<string, string>, Partial<ListState>][])('clamps an invalid value (%j) to the default', (params, expected) => {
    expect(parse(params)).toEqual({ ...DEFAULT_LIST_STATE, ...expected });
  });

  it('the API query names each filter only when it is set', () => {
    expect(toApiQuery(DEFAULT_LIST_STATE)).toEqual({ Page: 1, PageSize: 20, SortBy: 'nameAr', SortDirection: 'asc' });
    expect(toApiQuery({ ...DEFAULT_LIST_STATE, search: 'x', specialty: '5', clinic: '3', status: 'active' })).toEqual({
      Search: 'x',
      SpecialtyId: '5',
      ClinicId: '3',
      IsActive: true,
      Page: 1,
      PageSize: 20,
      SortBy: 'nameAr',
      SortDirection: 'asc',
    });
    expect(toApiQuery({ ...DEFAULT_LIST_STATE, clinic: '3', status: 'inactive' }).IsActive).toBe(false);
  });

  it('two states differing only in a filter are different', () => {
    expect(isSameListState(DEFAULT_LIST_STATE, { ...DEFAULT_LIST_STATE })).toBe(true);
    expect(isSameListState(DEFAULT_LIST_STATE, { ...DEFAULT_LIST_STATE, clinic: '3' })).toBe(false);
    expect(isSameListState(DEFAULT_LIST_STATE, { ...DEFAULT_LIST_STATE, specialty: '3' })).toBe(false);
  });
});
