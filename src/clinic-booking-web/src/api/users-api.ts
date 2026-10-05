import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { QueryParams, RequestBody, ResponseBody } from './types';

export type UserPage = ResponseBody<'/api/users', 'get', 200>;
export type UserSummary = UserPage['items'][number];
export type UserDetail = ResponseBody<'/api/users/{id}', 'get', 200>;
export type UserClinicPermissions = UserDetail['clinicPermissions'][number];
export type UserListQuery = QueryParams<'/api/users', 'get'>;
export type CreateUserRequest = RequestBody<'/api/users', 'post'>;
export type ResetPasswordRequest = RequestBody<'/api/users/{id}/reset-password', 'post'>;
export type AssignablePermissions = ResponseBody<'/api/permissions', 'get', 200>;

const BASE = '/api/users';
const segment = (value: number | string) => encodeURIComponent(String(value));

/**
 * One method per user-management endpoint (D57, D58). Nothing else talks to /api/users or /api/permissions.
 * A temporary password is only ever a request body here: it is never returned and never kept.
 */
@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  list(query: UserListQuery): Observable<UserPage> {
    let params = new HttpParams();
    for (const [name, value] of Object.entries(query)) {
      if (value !== undefined && value !== '') {
        params = params.set(name, String(value));
      }
    }
    return this.http.get<UserPage>(BASE, { params });
  }

  get(id: number | string): Observable<UserDetail> {
    return this.http.get<UserDetail>(`${BASE}/${segment(id)}`);
  }

  /** 201 with the new user; the user starts with a forced password change and no permission. */
  create(request: CreateUserRequest): Observable<UserDetail> {
    return this.http.post<UserDetail>(BASE, request);
  }

  disable(id: number | string): Observable<UserDetail> {
    return this.http.post<UserDetail>(`${BASE}/${segment(id)}/disable`, null);
  }

  enable(id: number | string): Observable<UserDetail> {
    return this.http.post<UserDetail>(`${BASE}/${segment(id)}/enable`, null);
  }

  /** 204. Sets a new temporary password and signs the user out everywhere (D58). */
  resetPassword(id: number | string, request: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>(`${BASE}/${segment(id)}/reset-password`, request);
  }

  /** A full replace, last write wins (no rowVersion, D57): the user ends up with exactly these. */
  replaceGlobalPermissions(id: number | string, permissions: string[]): Observable<UserDetail> {
    return this.http.put<UserDetail>(`${BASE}/${segment(id)}/global-permissions`, { permissions });
  }

  /** A full replace for one clinic; an empty list removes every grant in that clinic (D57). */
  replaceClinicPermissions(
    id: number | string,
    clinicId: number | string,
    permissions: string[],
  ): Observable<UserDetail> {
    return this.http.put<UserDetail>(`${BASE}/${segment(id)}/clinics/${segment(clinicId)}/permissions`, { permissions });
  }

  /** The names an administrator can assign: the single source for the editors (D59). */
  assignablePermissions(): Observable<AssignablePermissions> {
    return this.http.get<AssignablePermissions>('/api/permissions');
  }
}
