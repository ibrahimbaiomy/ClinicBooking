import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { RequestBody, ResponseBody } from './types';

export type LoginRequest = RequestBody<'/api/auth/login', 'post'>;
export type AuthResponse = ResponseBody<'/api/auth/login', 'post', 200>;
export type CurrentUser = ResponseBody<'/api/auth/me', 'get', 200>;

/** One method per auth endpoint (login, refresh, logout, me). Nothing else talks to /api/auth. */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', request);
  }

  /** Uses the HttpOnly refresh cookie; there is no body. */
  refresh(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/refresh', null);
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null);
  }

  me(): Observable<CurrentUser> {
    return this.http.get<CurrentUser>('/api/auth/me');
  }
}
