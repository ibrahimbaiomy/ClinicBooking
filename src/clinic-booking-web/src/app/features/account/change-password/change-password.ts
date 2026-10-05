import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { form, FormField, submit, validate } from '@angular/forms/signals';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from '../../../../api/auth-api';
import { ApiError, NETWORK_ERROR_KEY, parseApiError, UNEXPECTED_ERROR_KEY } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { safeReturnUrl } from '../../../core/auth/return-url';
import { SessionService } from '../../../core/auth/session.service';

/** Mirror of PasswordErrors.MaxLength on the API. */
export const PASSWORD_MAX_LENGTH = 128;
const LOCKED = 423;
const RATE_LIMITED = 429;
const MISMATCH_KEY = 'account.mismatch';

type Field = 'current' | 'next';

/** What the API calls each field in its 400 `errors` (camelCase). */
const API_FIELD: Record<string, Field | undefined> = { currentPassword: 'current', newPassword: 'next' };
const KEY_FIELD: Record<string, Field | undefined> = {
  'error.auth.current_password_incorrect': 'current',
  'error.auth.password_unchanged': 'next',
};

const EMPTY = { current: '', next: '', confirm: '' };

/**
 * Change the caller's own password (D58, D59). Forced variant: the account has a temporary password,
 * nothing else works until this is done, and the user then continues to the returnUrl. Voluntary variant
 * (from the header): the page stays and says it worked.
 *
 * Passwords: the three fields are cleared after every request, success or failure, and nothing about
 * them goes anywhere else: not the URL, not the router state, not storage, not a signal that outlives the
 * form. The server decides the policy and "unchanged"; the client checks only what saves a round trip.
 */
@Component({
  selector: 'cb-change-password',
  imports: [FormField, TranslocoPipe],
  templateUrl: './change-password.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChangePassword {
  private readonly api = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly injector = inject(Injector);
  protected readonly session = inject(SessionService);

  protected readonly forced = this.session.mustChangePassword;

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly errorBox = viewChild<ElementRef<HTMLElement>>('errorBox');
  private readonly statusBox = viewChild<ElementRef<HTMLElement>>('statusBox');

  protected readonly model = signal({ ...EMPTY });

  // Each message is a translation key (D26), shown through the transloco pipe in the template.
  protected readonly passwordForm = form(this.model, (path) => {
    validate(path.current, ({ value }) => {
      if (value() === '') return { kind: 'required', message: 'error.auth.current_password_required' };
      return value().length > PASSWORD_MAX_LENGTH ? { kind: 'maxLength', message: 'error.password.too_long' } : undefined;
    });
    validate(path.next, ({ value }) => {
      if (value() === '') return { kind: 'required', message: 'error.password.required' };
      return value().length > PASSWORD_MAX_LENGTH ? { kind: 'maxLength', message: 'error.password.too_long' } : undefined;
    });
    validate(path.confirm, ({ value, valueOf }) =>
      value() !== valueOf(path.next) ? { kind: 'mismatch', message: MISMATCH_KEY } : undefined,
    );
  });

  /** Keys the server put on a field; cleared when the user edits that field. Keys only, never a password. */
  protected readonly serverErrors = signal<Record<Field, string[]>>({ current: [], next: [] });

  protected readonly showCurrent = signal(false);
  protected readonly showNext = signal(false);

  private readonly failure = signal<ApiError | null>(null);
  protected readonly success = signal(false);

  protected readonly formErrorKey = computed(() => {
    const failure = this.failure();
    return failure === null ? null : this.messages.forError(failure);
  });

  /** Lockout and rate limiting: a rough "try again in N minutes" built from Retry-After. */
  protected readonly retryMinutes = computed(() => {
    const failure = this.failure();
    const waits = failure?.status === LOCKED || failure?.status === RATE_LIMITED;
    return waits && failure.retryAfterSeconds !== null ? Math.max(1, Math.ceil(failure.retryAfterSeconds / 60)) : null;
  });

  protected readonly correlationId = computed(() => {
    const failure = this.failure();
    return failure !== null && (failure.key === UNEXPECTED_ERROR_KEY || failure.key === NETWORK_ERROR_KEY)
      ? failure.correlationId
      : null;
  });

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());
  }

  protected toggleCurrent(): void {
    this.showCurrent.update((shown) => !shown);
  }

  protected toggleNext(): void {
    this.showNext.update((shown) => !shown);
  }

  protected clearServerErrors(field: Field): void {
    if (this.serverErrors()[field].length > 0) {
      this.serverErrors.update((errors) => ({ ...errors, [field]: [] }));
    }
  }

  /** A server message that is not a known key is replaced by error.unexpected (never raw text). */
  protected messageKey(message: string | undefined): string {
    return message === MISMATCH_KEY ? MISMATCH_KEY : this.messages.keyFor(message ?? '');
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.success.set(false);

    await submit(this.passwordForm, {
      action: async () => {
        this.failure.set(null);
        this.serverErrors.set({ current: [], next: [] });

        // Read once, sent once. The form is emptied before the answer comes back, whatever it is.
        const { current, next } = this.model();
        const wasForced = this.forced();
        this.emptyTheForm();
        let error: ApiError | null = null;
        try {
          await firstValueFrom(this.api.changePassword({ currentPassword: current, newPassword: next }));
        } catch (caught: unknown) {
          error = parseApiError(caught);
        }

        if (error !== null) {
          this.failed(error);
          return undefined;
        }

        await this.succeeded(wasForced);
        return undefined;
      },
      onInvalid: () => {
        const first = (['current', 'next', 'confirm'] as const).find((field) => this.passwordForm[field]().invalid());
        if (first !== undefined) {
          this.passwordForm[first]().focusBoundControl();
        }
      },
    });
  }

  /** The password fields are emptied, shown as hidden again, and the form is pristine. */
  private emptyTheForm(): void {
    this.passwordForm().reset({ ...EMPTY });
    this.showCurrent.set(false);
    this.showNext.set(false);
  }

  private async succeeded(wasForced: boolean): Promise<void> {
    // The session must match the server: mustChangePassword is now false there.
    if (!(await this.session.refreshUser())) {
      this.session.clearPasswordChangeRequired();
    }

    if (wasForced) {
      await this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
      return;
    }

    this.success.set(true);
    afterNextRender(() => this.statusBox()?.nativeElement.focus(), { injector: this.injector });
  }

  /** Puts each server message on its field; anything else is a form-level message. */
  private failed(error: ApiError): void {
    const placed: Record<Field, string[]> = { current: [], next: [] };
    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      const field = API_FIELD[name];
      if (field !== undefined) {
        placed[field].push(...keys);
      }
    }

    const named = KEY_FIELD[error.key];
    if (named !== undefined && placed[named].length === 0) {
      placed[named].push(error.key);
    }

    this.serverErrors.set(placed);

    const onAField = placed.current.length + placed.next.length > 0;
    if (onAField) {
      const field: Field = placed.current.length > 0 ? 'current' : 'next';
      afterNextRender(() => this.passwordForm[field]().focusBoundControl(), { injector: this.injector });
    } else {
      this.failure.set(error);
      afterNextRender(() => this.errorBox()?.nativeElement.focus(), { injector: this.injector });
    }
  }
}
