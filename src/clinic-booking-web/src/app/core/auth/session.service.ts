import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  catchError,
  finalize,
  firstValueFrom,
  map,
  Observable,
  of,
  shareReplay,
  switchMap,
  tap,
  throwError,
  timeout,
  timer,
} from 'rxjs';
import { AuthApi, CurrentUser } from '../../../api/auth-api';
import { parseApiError } from './api-error';
import { safeReturnUrl } from './return-url';

/** The change-password page (D59). */
export const CHANGE_PASSWORD_URL = '/change-password';

export type SessionState = 'unknown' | 'authenticated' | 'anonymous';

/** The startup restore must never hang the first render (D52). */
export const RESTORE_TIMEOUT_MS = 10_000;

/**
 * Within Auth:ReuseGraceSeconds (10 s) a second tab's refresh may lose the race and get a 401 while
 * the winner's cookie is already in the shared jar. One retry after this delay (plus jitter) tells
 * that case from a really ended session (D52).
 */
export const GRACE_RETRY_DELAY_MS = 1000;
export const GRACE_RETRY_JITTER_MS = 500;

/**
 * The session (D29, D48): the access token lives in this object's memory only, never in
 * localStorage or sessionStorage. The refresh cookie is HttpOnly and invisible to scripts.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly api = inject(AuthApi);
  private readonly router = inject(Router);

  private token: string | null = null;
  private inFlight: Observable<string> | null = null;
  private restoring: Promise<void> | null = null;
  private redirectingToChangePassword = false;

  private readonly stateSignal = signal<SessionState>('unknown');
  private readonly userSignal = signal<CurrentUser | null>(null);
  private readonly expiredSignal = signal(false);

  readonly state = this.stateSignal.asReadonly();
  readonly user = this.userSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.stateSignal() === 'authenticated');
  readonly permissions = computed(() => new Set(this.userSignal()?.permissions ?? []));
  /** True while the account has a temporary password: only change-password, sign-out and the language switcher work (D58, D59). */
  readonly mustChangePassword = computed(() => this.userSignal()?.mustChangePassword === true);
  /** Clinic-scoped permissions by clinic id (ids come as number or string: int64 typing, D59). */
  readonly clinicPermissions = computed(() => {
    const byClinic = new Map<number, Set<string>>();
    for (const entry of this.userSignal()?.clinicPermissions ?? []) {
      byClinic.set(Number(entry.clinicId), new Set(entry.permissions));
    }
    return byClinic;
  });
  /** True once, after a session ended mid-use; the login page shows a notice and clears it. */
  readonly expired = this.expiredSignal.asReadonly();

  /** UX only: the API enforces every permission on every call. */
  can(permission: string): boolean {
    return this.permissions().has(permission);
  }

  /** UX only: a clinic-scoped permission held in that clinic. A global permission never satisfies it (D57). */
  canIn(permission: string, clinicId: number | string): boolean {
    return this.clinicPermissions().get(Number(clinicId))?.has(permission) ?? false;
  }

  /** UX only: a clinic-scoped permission held in at least one clinic (for navigation and lists). */
  canInAny(permission: string): boolean {
    return [...this.clinicPermissions().values()].some((held) => held.has(permission));
  }

  /** Whether an id from the API (number or string) is the signed-in user's own. */
  isCurrentUser(id: number | string): boolean {
    const current = this.userSignal()?.id;
    return current !== undefined && Number(current) === Number(id);
  }

  accessToken(): string | null {
    return this.token;
  }

  clearExpiredNotice(): void {
    this.expiredSignal.set(false);
  }

  /**
   * Silent restore at startup. Never rejects and never hangs: any failure (401, 429, network,
   * timeout) means anonymous at once. No grace retry here: a logged-out visitor must not wait.
   */
  restore(): Promise<void> {
    this.restoring ??= firstValueFrom(
      this.api.refresh().pipe(
        tap((response) => (this.token = response.accessToken)),
        switchMap(() => this.api.me()),
        timeout(RESTORE_TIMEOUT_MS),
        tap((user) => this.setUser(user)),
        map(() => undefined),
        catchError(() => {
          this.clear();
          return of(undefined);
        }),
      ),
    );
    return this.restoring;
  }

  /** Resolves once the startup restore has settled, so guards never see `unknown`. */
  whenReady(): Promise<void> {
    return this.restoring ?? Promise.resolve();
  }

  login(userName: string, password: string): Observable<CurrentUser> {
    return this.api.login({ userName, password }).pipe(
      tap((response) => (this.token = response.accessToken)),
      switchMap(() => this.api.me()),
      tap((user) => this.setUser(user)),
      catchError((error: unknown) => {
        this.clear();
        return throwError(() => error);
      }),
    );
  }

  /** Clears local state whether or not the server call succeeds. */
  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.api.logout());
    } catch {
      // The server may be unreachable; the local session still ends.
    } finally {
      this.clear();
    }
  }

  /**
   * For the interceptor after a 401 (D52). If another request already refreshed (the token this
   * request carried is no longer current) it reuses that token; otherwise all callers share one
   * refresh. A really ended session is cleared and sent to the login page.
   */
  refreshAfterUnauthorized(usedToken: string | null): Observable<string> {
    if (this.token !== null && this.token !== usedToken) {
      return of(this.token);
    }

    this.inFlight ??= this.refreshWithGraceRetry().pipe(
      finalize(() => (this.inFlight = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.inFlight;
  }

  /**
   * Reads the current user again, so the session matches the server (after a password change or a
   * change of the caller's own permissions). Returns false when the call failed; the user is then unchanged.
   */
  async refreshUser(): Promise<boolean> {
    try {
      this.setUser(await firstValueFrom(this.api.me()));
      return true;
    } catch {
      return false;
    }
  }

  /**
   * The server answered 403 error.auth.password_change_required: remember it and send the user to the
   * change-password page, remembering where they were. Never navigates when already there (no loop), and
   * concurrent 403s navigate once (D58, D59).
   */
  requirePasswordChange(): void {
    const user = this.userSignal();
    if (user !== null && !user.mustChangePassword) {
      this.userSignal.set({ ...user, mustChangePassword: true });
    }

    if (this.redirectingToChangePassword || this.router.url.split(/[?#]/)[0] === CHANGE_PASSWORD_URL) {
      return;
    }

    this.redirectingToChangePassword = true;
    const returnUrl = safeReturnUrl(this.router.url);
    void this.router
      .navigate([CHANGE_PASSWORD_URL], { queryParams: returnUrl === '/' ? {} : { returnUrl } })
      .finally(() => (this.redirectingToChangePassword = false));
  }

  /** After a successful change when /me could not be read: the next call tells the truth (403 re-forces it). */
  clearPasswordChangeRequired(): void {
    const user = this.userSignal();
    if (user !== null && user.mustChangePassword) {
      this.userSignal.set({ ...user, mustChangePassword: false });
    }
  }

  /** The session has ended while in use: clear it and go to login, remembering where the user was. */
  expire(): void {
    const current = this.router.url;
    this.clear();
    this.expiredSignal.set(true);
    const returnUrl = safeReturnUrl(current);
    void this.router.navigate(['/login'], {
      queryParams: returnUrl === '/' ? {} : { returnUrl },
    });
  }

  private refreshWithGraceRetry(): Observable<string> {
    return this.api.refresh().pipe(
      catchError((error: unknown) =>
        parseApiError(error).status === 401
          ? timer(GRACE_RETRY_DELAY_MS + Math.random() * GRACE_RETRY_JITTER_MS).pipe(
              switchMap(() => this.api.refresh()),
            )
          : throwError(() => error),
      ),
      tap((response) => (this.token = response.accessToken)),
      map((response) => response.accessToken),
      catchError((error: unknown) => {
        // Only a 401 means the session is over; 429, 5xx and network failures are transient.
        if (parseApiError(error).status === 401) {
          this.expire();
        }
        return throwError(() => error);
      }),
    );
  }

  private setUser(user: CurrentUser): void {
    this.userSignal.set(user);
    this.stateSignal.set('authenticated');
  }

  private clear(): void {
    this.token = null;
    this.userSignal.set(null);
    this.stateSignal.set('anonymous');
  }
}
