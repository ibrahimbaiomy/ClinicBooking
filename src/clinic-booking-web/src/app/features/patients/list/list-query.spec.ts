import { convertToParamMap } from '@angular/router';
import { DEFAULT_LIST_STATE, parseListState, toApiQuery, toQueryParams } from './list-query';

const parse = (params: Record<string, string>) => parseListState(convertToParamMap(params));

describe('patients list-query (D63)', () => {
  it('an empty URL is the default state: name ascending', () => {
    expect(parse({})).toEqual(DEFAULT_LIST_STATE);
    expect(toQueryParams(DEFAULT_LIST_STATE)).toEqual({});
    expect(toApiQuery(DEFAULT_LIST_STATE)).toEqual({ Page: 1, PageSize: 20, SortBy: 'name', SortDirection: 'asc' });
  });

  it('round-trips a phone search, paging and the creation sort', () => {
    const params = { q: '010 123', page: '2', size: '50', sort: 'createdAt', dir: 'desc' };
    const state = parse(params);

    expect(toQueryParams(state)).toEqual({ ...params, page: 2, size: 50 });
    expect(toApiQuery(state).Search).toBe('010 123');
  });

  it('clamps a sort field the patients list does not have', () => {
    expect(parse({ sort: 'nameAr' }).sortBy).toBe('name');
  });
});
