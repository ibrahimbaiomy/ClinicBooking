import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthApi } from './auth-api';
import { UsersApi } from './users-api';

describe('UsersApi (D57, D58)', () => {
  let api: UsersApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(UsersApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list sends the typed PascalCase query, IsActive included, and omits empty values', () => {
    api.list({ Search: 'ann', IsActive: false, Page: 2, PageSize: 20, SortBy: 'createdAt', SortDirection: 'desc' }).subscribe();

    const request = http.expectOne((r) => r.url === '/api/users');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('Search')).toBe('ann');
    expect(request.request.params.get('IsActive')).toBe('false');
    expect(request.request.params.get('Page')).toBe('2');
    expect(request.request.params.get('PageSize')).toBe('20');
    expect(request.request.params.get('SortBy')).toBe('createdAt');
    expect(request.request.params.get('SortDirection')).toBe('desc');
    request.flush({ items: [], page: 2, pageSize: 20, totalCount: 0 });
  });

  it('list leaves out an absent or empty search and an absent status', () => {
    api.list({ Search: '', Page: 1 }).subscribe();

    const request = http.expectOne((r) => r.url === '/api/users');
    expect(request.request.params.has('Search')).toBe(false);
    expect(request.request.params.has('IsActive')).toBe(false);
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });

  it('get reads one user by id, encoded', () => {
    api.get(7).subscribe();
    api.get('a/b').subscribe();

    expect(http.expectOne('/api/users/7').request.method).toBe('GET');
    http.expectOne('/api/users/a%2Fb').flush({});
    http.match('/api/users/7').forEach((r) => r.flush({}));
  });

  it('create posts the user name and the temporary password as the body', () => {
    api.create({ userName: 'ann', temporaryPassword: 'Temp-Password-1' }).subscribe();

    const request = http.expectOne('/api/users');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ userName: 'ann', temporaryPassword: 'Temp-Password-1' });
    request.flush({});
  });

  it.each(['disable', 'enable'] as const)('%s posts to its own route with no body', (action) => {
    api[action](5).subscribe();

    const request = http.expectOne(`/api/users/5/${action}`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    request.flush({});
  });

  it('resetPassword posts the temporary password as the body and expects no content', () => {
    let done = false;
    api.resetPassword(5, { temporaryPassword: 'Temp-Password-2' }).subscribe(() => (done = true));

    const request = http.expectOne('/api/users/5/reset-password');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ temporaryPassword: 'Temp-Password-2' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(done).toBe(true);
  });

  it('replaceGlobalPermissions puts the full list, with no row version', () => {
    api.replaceGlobalPermissions(5, ['users.manage']).subscribe();

    const request = http.expectOne('/api/users/5/global-permissions');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ permissions: ['users.manage'] });
    request.flush({});
  });

  it('replaceClinicPermissions puts the full list for one clinic; an empty list is sent as is', () => {
    api.replaceClinicPermissions(5, 9, []).subscribe();

    const request = http.expectOne('/api/users/5/clinics/9/permissions');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ permissions: [] });
    request.flush({});
  });

  it('assignablePermissions reads /api/permissions', () => {
    api.assignablePermissions().subscribe();

    const request = http.expectOne('/api/permissions');
    expect(request.request.method).toBe('GET');
    request.flush({ global: [], clinicScoped: [] });
  });

  it('AuthApi.changePassword posts both passwords as the body to /api/auth/change-password', () => {
    TestBed.inject(AuthApi).changePassword({ currentPassword: 'old-value', newPassword: 'new-value' }).subscribe();

    const request = http.expectOne('/api/auth/change-password');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ currentPassword: 'old-value', newPassword: 'new-value' });
    request.flush(null, { status: 204, statusText: 'No Content' });
  });
});
