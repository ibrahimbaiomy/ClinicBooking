import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Routes } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from '../app/core/auth/auth.interceptor';
import { SessionService } from '../app/core/auth/session.service';
import { provideTestTransloco } from './transloco-testing';

@Component({ selector: 'cb-test-stub', templateUrl: './stub.html' })
export class Stub {}

export const STUB_ROUTES: Routes = [
  { path: 'login', component: Stub },
  { path: '**', component: Stub },
];

/** Real interceptor and session, with HTTP replaced by HttpTestingController and stub routes. */
export function provideAuthTesting(routes: Routes = STUB_ROUTES) {
  return [
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideHttpClientTesting(),
    provideTestTransloco(),
  ];
}

export const tokenResponse = (accessToken: string) => ({ accessToken, expiresAt: '2030-01-01T00:00:00Z' });

export function flushProblem(
  request: TestRequest,
  status: number,
  title: string,
  extras: { headers?: Record<string, string>; body?: Record<string, unknown> } = {},
): void {
  request.flush(
    { type: 'about:blank', title, status, correlationId: 'corr-123', ...extras.body },
    { status, statusText: String(status), headers: { 'Content-Type': 'application/problem+json', ...extras.headers } },
  );
}

/** Signs in through the real login flow (login, then /me). */
export async function signIn(permissions: string[] = [], token = 'token-1'): Promise<void> {
  const session = TestBed.inject(SessionService);
  const http = TestBed.inject(HttpTestingController);
  const done = firstValueFrom(session.login('someone', 'a-test-value'));
  http.expectOne('/api/auth/login').flush(tokenResponse(token));
  http.expectOne('/api/auth/me').flush({ id: 1, userName: 'someone', permissions });
  await done;
}
