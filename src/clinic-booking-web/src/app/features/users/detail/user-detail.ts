import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { AssignablePermissions, UserDetail, UsersApi } from '../../../../api/users-api';
import { parseApiError } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { Permissions } from '../../../core/auth/permissions';
import { SessionService } from '../../../core/auth/session.service';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { ConfirmDialog } from '../../../shared/ui/confirm-dialog';
import { UsersSession } from '../users-session';
import { ClinicPermissionsEditor } from './clinic-permissions-editor';
import { GlobalPermissionsEditor, PermissionsSaved } from './global-permissions-editor';

/** Mirrors PasswordErrors.MaxLength: the policy itself is the server's (D48, D57). */
export const PASSWORD_MAX_LENGTH = 128;

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';

/**
 * One user (D57, D58, D59): the summary, enable and disable, reset password, and the two permission
 * editors. Every change is followed by the server's state. The administrator's own row has no disable
 * or reset (the API refuses them too), and saving their own permissions refreshes the session.
 */
@Component({
  selector: 'cb-user-detail',
  imports: [RouterLink, TranslocoPipe, IntlPipe, ConfirmDialog, GlobalPermissionsEditor, ClinicPermissionsEditor],
  templateUrl: './user-detail.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserDetailPage {
  private readonly api = inject(UsersApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(UsersSession);
  private readonly session = inject(SessionService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private readonly id = this.route.snapshot.paramMap.get('id');
  protected readonly listQuery = this.memory.lastQuery;
  protected readonly maxPasswordLength = PASSWORD_MAX_LENGTH;

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly detail = signal<UserDetail | null>(null);
  protected readonly assignable = signal<AssignablePermissions | null>(null);
  protected readonly assignableFailed = signal(false);

  protected readonly flash = signal<string | null>(null);
  protected readonly actionErrorKey = signal<string | null>(null);
  protected readonly enabling = signal(false);

  /** Your own row: the API refuses disable and reset for it (422), so the buttons are not offered. */
  protected readonly isOwn = computed(() => {
    const detail = this.detail();
    return detail !== null && this.session.isCurrentUser(detail.id);
  });

  // ---- disable
  protected readonly pendingDisable = signal(false);
  protected readonly disabling = signal(false);
  protected readonly disableErrorKey = signal<string | null>(null);

  // ---- reset password: the field is emptied on every close and right after the request is sent
  protected readonly pendingReset = signal(false);
  protected readonly resetting = signal(false);
  protected readonly resetPassword = signal('');
  protected readonly resetShown = signal(false);
  protected readonly resetFieldErrors = signal<string[]>([]);
  protected readonly resetErrorKey = signal<string | null>(null);

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly statusBox = viewChild<ElementRef<HTMLElement>>('statusBox');

  constructor() {
    this.flash.set(this.memory.takeFlash());
    afterNextRender(() => this.heading()?.nativeElement.focus());

    if (this.id !== null && /^\d+$/.test(this.id)) {
      this.load();
    } else {
      this.loadState.set('notFound');
    }
  }

  protected load(): void {
    this.loadState.set('loading');
    this.api
      .get(this.id as string)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.detail.set(detail);
          this.loadState.set('ready');
        },
        error: (error: unknown) => {
          const failure = parseApiError(error);
          this.loadErrorKey.set(this.messages.forError(failure));
          this.loadState.set(failure.status === 404 ? 'notFound' : 'error');
        },
      });
    this.loadAssignable();
  }

  protected loadAssignable(): void {
    this.assignableFailed.set(false);
    this.api
      .assignablePermissions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (assignable) => this.assignable.set(assignable),
        error: () => this.assignableFailed.set(true),
      });
  }

  // ---- enable and disable
  protected askDisable(): void {
    this.disableErrorKey.set(null);
    this.pendingDisable.set(true);
  }

  protected cancelDisable(): void {
    if (!this.disabling()) {
      this.pendingDisable.set(false);
    }
  }

  protected async confirmDisable(): Promise<void> {
    this.disabling.set(true);
    try {
      this.detail.set(await firstValueFrom(this.api.disable(this.id as string)));
      this.pendingDisable.set(false);
      this.showStatus('users.flash.disabled');
    } catch (error: unknown) {
      // Stays in the dialog, translated: self-disable and last-administrator (422) are shown clearly.
      this.disableErrorKey.set(this.messages.forError(parseApiError(error)));
    } finally {
      this.disabling.set(false);
    }
  }

  protected async enable(): Promise<void> {
    this.actionErrorKey.set(null);
    this.enabling.set(true);
    try {
      this.detail.set(await firstValueFrom(this.api.enable(this.id as string)));
      this.showStatus('users.flash.enabled');
    } catch (error: unknown) {
      this.actionErrorKey.set(this.messages.forError(parseApiError(error)));
    } finally {
      this.enabling.set(false);
    }
  }

  // ---- reset password
  protected askReset(): void {
    this.closeReset(false);
    this.pendingReset.set(true);
  }

  protected onResetInput(event: Event): void {
    this.resetPassword.set((event.target as HTMLInputElement).value);
    this.resetFieldErrors.set([]);
  }

  protected toggleResetShown(): void {
    this.resetShown.update((shown) => !shown);
  }

  /** Cancel, Esc and every other way of closing empties the field. */
  protected cancelReset(): void {
    if (!this.resetting()) {
      this.closeReset(false);
    }
  }

  protected async confirmReset(): Promise<void> {
    const password = this.resetPassword();
    if (password === '' || password.length > PASSWORD_MAX_LENGTH) {
      this.resetFieldErrors.set([password === '' ? 'error.password.required' : 'error.password.too_long']);
      return;
    }

    // Read once, sent once; the field is emptied before the answer comes back.
    this.resetPassword.set('');
    this.resetFieldErrors.set([]);
    this.resetErrorKey.set(null);
    this.resetting.set(true);
    try {
      await firstValueFrom(this.api.resetPassword(this.id as string, { temporaryPassword: password }));
      this.closeReset(true);
      this.showStatus('users.flash.password_reset');
    } catch (error: unknown) {
      const failure = parseApiError(error);
      const keys = failure.fieldErrors['temporaryPassword'];
      if (keys !== undefined && keys.length > 0) {
        this.resetFieldErrors.set(keys.map((key) => this.messages.keyFor(key)));
      } else {
        this.resetErrorKey.set(this.messages.forError(failure));
      }
    } finally {
      this.resetting.set(false);
    }
  }

  private closeReset(done: boolean): void {
    this.resetPassword.set('');
    this.resetShown.set(false);
    this.resetFieldErrors.set([]);
    this.resetErrorKey.set(null);
    this.pendingReset.set(false);
    if (done) {
      this.resetting.set(false);
    }
  }

  // ---- permissions
  /** A permission editor saved: show the server's state, and keep the session honest if it was your own. */
  protected async onSaved(saved: PermissionsSaved): Promise<void> {
    this.detail.set(saved.detail);
    this.showStatus(saved.flash);

    if (this.isOwn()) {
      await this.session.refreshUser();
      if (!this.session.can(Permissions.UsersManage)) {
        // This page, and the whole area, is now forbidden: leave it instead of showing errors.
        await this.router.navigateByUrl('/');
      }
    }
  }

  private showStatus(key: string): void {
    this.flash.set(key);
    afterNextRender(() => this.statusBox()?.nativeElement.focus(), { injector: this.injector });
  }
}
