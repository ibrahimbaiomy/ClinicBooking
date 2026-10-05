import { HttpErrorResponse, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { parseApiError } from './api-error';
import { SessionService } from './session.service';

const UNAUTHORIZED_KEY = 'error.auth.unauthorized';
const PASSWORD_CHANGE_REQUIRED_KEY = 'error.auth.password_change_required';
const AUTH_ENDPOINTS = new Set(['/api/auth/login', '/api/auth/refresh', '/api/auth/logout']);

/** Only same-origin /api requests carry the token: translation files and other URLs never do. */
export function isApiRequest(url: string): boolean {
  const parsed = parseUrl(url);
  return parsed !== null && parsed.origin === location.origin && parsed.pathname.startsWith('/api/');
}

function isAuthEndpoint(url: string): boolean {
  const parsed = parseUrl(url);
  return parsed !== null && AUTH_ENDPOINTS.has(parsed.pathname.toLowerCase());
}

function parseUrl(url: string): URL | null {
  try {
    return new URL(url, location.origin);
  } catch {
    return null;
  }
}

const withToken = (request: HttpRequest<unknown>, token: string) =>
  request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

function isUnauthorized(error: unknown): boolean {
  return (
    error instanceof HttpErrorResponse &&
    error.status === 401 &&
    parseApiError(error).key === UNAUTHORIZED_KEY
  );
}

function retryOnce(request: HttpRequest<unknown>, next: HttpHandlerFn, session: SessionService, usedToken: string) {
  return session.refreshAfterUnauthorized(usedToken).pipe(
    switchMap((token) =>
      next(withToken(request, token)).pipe(
        catchError((error: unknown) => {
          // A second 401 right after a successful refresh: the session cannot be saved.
          if (isUnauthorized(error)) {
            session.expire();
          }
          return throwError(() => error);
        }),
      ),
    ),
  );
}

function isPasswordChangeRequired(error: unknown): boolean {
  return (
    error instanceof HttpErrorResponse &&
    error.status === 403 &&
    parseApiError(error).key === PASSWORD_CHANGE_REQUIRED_KEY
  );
}

/**
 * Adds the bearer token and, on a 401 `error.auth.unauthorized`, refreshes once (shared by every
 * concurrent 401) and retries the request once. login, refresh and logout never get a header and
 * never trigger a refresh (D52). A 403 `error.auth.password_change_required` sends the user to the
 * change-password page once (D59).
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isApiRequest(request.url) || isAuthEndpoint(request.url)) {
    return next(request);
  }

  const session = inject(SessionService);
  const usedToken = session.accessToken();

  return next(usedToken === null ? request : withToken(request, usedToken)).pipe(
    catchError((error: unknown) => {
      if (isPasswordChangeRequired(error)) {
        // The account has a temporary password (D58): everything but change-password is refused.
        // The failure still reaches the caller; the user is sent to the change-password page (D59).
        session.requirePasswordChange();
        return throwError(() => error);
      }
      if (usedToken === null || !isUnauthorized(error)) {
        return throwError(() => error);
      }
      return retryOnce(request, next, session, usedToken);
    }),
  );
};
