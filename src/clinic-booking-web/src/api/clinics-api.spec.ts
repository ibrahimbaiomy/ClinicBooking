import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ClinicsApi } from './clinics-api';

describe('ClinicsApi', () => {
  let api: ClinicsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ClinicsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list sends the typed PascalCase query and omits empty values', () => {
    api.list({ Search: 'نيل', Page: 2, PageSize: 20, SortBy: 'nameAr', SortDirection: 'desc' }).subscribe();

    const request = http.expectOne((r) => r.url === '/api/clinics');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('Search')).toBe('نيل');
    expect(request.request.params.get('Page')).toBe('2');
    expect(request.request.params.get('PageSize')).toBe('20');
    expect(request.request.params.get('SortBy')).toBe('nameAr');
    expect(request.request.params.get('SortDirection')).toBe('desc');
    request.flush({ items: [], page: 2, pageSize: 20, totalCount: 0 });
  });

  it('list leaves out an absent or empty search', () => {
    api.list({ Page: 1, PageSize: 10 }).subscribe();
    api.list({ Search: '', Page: 1 }).subscribe();

    const requests = http.match((r) => r.url === '/api/clinics');
    expect(requests).toHaveLength(2);
    requests.forEach((r) => {
      expect(r.request.params.has('Search')).toBe(false);
      r.flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    });
  });

  it('get reads one clinic by id', () => {
    api.get(7).subscribe();

    const request = http.expectOne('/api/clinics/7');
    expect(request.request.method).toBe('GET');
    request.flush({});
  });

  it('create posts the names, the address and the phone as given', () => {
    api
      .create({ nameAr: 'عيادة النيل', nameEn: 'Nile Clinic', address: '12 شارع النيل', phone: '٠١٠ ١٢٣٤ ٥٦٧٨' })
      .subscribe();

    const request = http.expectOne('/api/clinics');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      nameAr: 'عيادة النيل',
      nameEn: 'Nile Clinic',
      address: '12 شارع النيل',
      phone: '٠١٠ ١٢٣٤ ٥٦٧٨',
    });
    request.flush({}, { status: 201, statusText: 'Created' });
  });

  it('create sends null for an absent address and phone', () => {
    api.create({ nameAr: 'ا', nameEn: 'a', address: null, phone: null }).subscribe();

    const request = http.expectOne('/api/clinics');
    expect(request.request.body).toEqual({ nameAr: 'ا', nameEn: 'a', address: null, phone: null });
    request.flush({}, { status: 201, statusText: 'Created' });
  });

  it('update puts everything with the rowVersion it received (a full replace)', () => {
    api
      .update('7', { nameAr: 'ا', nameEn: 'a', address: null, phone: '+201012345678', rowVersion: 'AAAAAAAB' })
      .subscribe();

    const request = http.expectOne('/api/clinics/7');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      nameAr: 'ا',
      nameEn: 'a',
      address: null,
      phone: '+201012345678',
      rowVersion: 'AAAAAAAB',
    });
    request.flush({});
  });

  it('delete sends DELETE', () => {
    api.delete('7').subscribe();

    const request = http.expectOne('/api/clinics/7');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('encodes the id in the path', () => {
    api.get('a/b').subscribe();

    http.expectOne('/api/clinics/a%2Fb').flush({});
  });
});
