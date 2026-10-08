import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PatientsApi } from './patients-api';

describe('PatientsApi (D63)', () => {
  let api: PatientsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(PatientsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list sends the PascalCase query and omits an empty search', () => {
    api.list({ Search: '010', Page: 2, PageSize: 20, SortBy: 'name', SortDirection: 'desc' }).subscribe();
    api.list({ Search: '', Page: 1 }).subscribe();

    const [searched, plain] = http.match((r) => r.url === '/api/patients');
    expect(searched.request.params.get('Search')).toBe('010');
    expect(searched.request.params.get('SortBy')).toBe('name');
    expect(plain.request.params.has('Search')).toBe(false);
  });

  it('create and update send the duplicate confirmation when given', () => {
    api.create({ name: 'سارة', phone: '01012345678', confirmDuplicatePhone: true }).subscribe();
    api.update(7, { name: 'سارة', phone: '+201012345678', rowVersion: 'AAAAAAAAAAE=', confirmDuplicatePhone: null }).subscribe();

    expect(http.expectOne({ method: 'POST', url: '/api/patients' }).request.body).toEqual({
      name: 'سارة',
      phone: '01012345678',
      confirmDuplicatePhone: true,
    });
    expect(http.expectOne({ method: 'PUT', url: '/api/patients/7' }).request.body.rowVersion).toBe('AAAAAAAAAAE=');
  });

  it('get and delete address one patient', () => {
    api.get(7).subscribe();
    api.delete(7).subscribe();

    expect(http.expectOne({ method: 'GET', url: '/api/patients/7' })).toBeTruthy();
    expect(http.expectOne({ method: 'DELETE', url: '/api/patients/7' })).toBeTruthy();
  });
});
