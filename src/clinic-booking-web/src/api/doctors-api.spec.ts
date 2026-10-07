import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DoctorsApi } from './doctors-api';

describe('DoctorsApi', () => {
  let api: DoctorsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(DoctorsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list sends the typed PascalCase query with the filters and omits absent ones', () => {
    api
      .list({ Search: 'أحمد', ClinicId: 3, SpecialtyId: 5, IsActive: false, Page: 2, PageSize: 20, SortBy: 'nameAr', SortDirection: 'asc' })
      .subscribe();
    api.list({ Page: 1, PageSize: 100 }).subscribe();

    const [filtered, plain] = http.match((r) => r.url === '/api/doctors');
    expect(filtered.request.params.get('Search')).toBe('أحمد');
    expect(filtered.request.params.get('ClinicId')).toBe('3');
    expect(filtered.request.params.get('SpecialtyId')).toBe('5');
    expect(filtered.request.params.get('IsActive')).toBe('false');
    expect(filtered.request.params.get('SortBy')).toBe('nameAr');
    expect(plain.request.params.keys().sort()).toEqual(['Page', 'PageSize']);
    filtered.flush({ items: [], page: 2, pageSize: 20, totalCount: 0 });
    plain.flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
  });

  it('get, update and delete address one doctor', () => {
    api.get(7).subscribe();
    api.update(7, { nameAr: 'أ', nameEn: 'A', specialtyIds: [1], rowVersion: 'AAAAAAAAAAA=' }).subscribe();
    api.delete(7).subscribe();

    expect(http.expectOne({ method: 'GET', url: '/api/doctors/7' })).toBeTruthy();
    const put = http.expectOne({ method: 'PUT', url: '/api/doctors/7' });
    expect(put.request.body).toEqual({ nameAr: 'أ', nameEn: 'A', specialtyIds: [1], rowVersion: 'AAAAAAAAAAA=' });
    expect(http.expectOne({ method: 'DELETE', url: '/api/doctors/7' })).toBeTruthy();
  });

  it('create posts names, specialties, clinics and the slot duration', () => {
    api.create({ nameAr: 'أحمد', nameEn: 'Ahmed', specialtyIds: [1, 2], clinicIds: [3], slotMinutes: 15 }).subscribe();

    const request = http.expectOne({ method: 'POST', url: '/api/doctors' });
    expect(request.request.body).toEqual({ nameAr: 'أحمد', nameEn: 'Ahmed', specialtyIds: [1, 2], clinicIds: [3], slotMinutes: 15 });
  });

  it('the assignment endpoints post to the doctor and clinic', () => {
    api.addClinic(7, 3).subscribe();
    api.activateClinic(7, 3).subscribe();
    api.deactivateClinic(7, 3).subscribe();

    expect(http.expectOne({ method: 'POST', url: '/api/doctors/7/clinics/3' }).request.body).toBeNull();
    expect(http.expectOne({ method: 'POST', url: '/api/doctors/7/clinics/3/activate' })).toBeTruthy();
    expect(http.expectOne({ method: 'POST', url: '/api/doctors/7/clinics/3/deactivate' })).toBeTruthy();
  });

  it('working hours are read and replaced per doctor and clinic', () => {
    api.getWorkingHours(7, 3).subscribe();
    api.replaceWorkingHours(7, 3, { periods: [{ dayOfWeek: 6, start: '09:00', end: '13:00' }], rowVersion: 'AAAAAAAAAAA=' }).subscribe();

    expect(http.expectOne({ method: 'GET', url: '/api/doctors/7/clinics/3/working-hours' })).toBeTruthy();
    const put = http.expectOne({ method: 'PUT', url: '/api/doctors/7/clinics/3/working-hours' });
    expect(put.request.body).toEqual({ periods: [{ dayOfWeek: 6, start: '09:00', end: '13:00' }], rowVersion: 'AAAAAAAAAAA=' });
  });

  it('a slot-duration change posts the minutes and the date', () => {
    api.changeSlotDuration(7, { slotMinutes: 30, effectiveFrom: '2026-07-10' }).subscribe();

    const request = http.expectOne({ method: 'POST', url: '/api/doctors/7/slot-durations' });
    expect(request.request.body).toEqual({ slotMinutes: 30, effectiveFrom: '2026-07-10' });
  });

  it('ids are encoded into the path', () => {
    api.get('7/../x').subscribe();

    expect(http.expectOne('/api/doctors/7%2F..%2Fx')).toBeTruthy();
  });
});
