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
import { form, FormField, maxLength, required, submit } from '@angular/forms/signals';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { ApiError, NETWORK_ERROR_KEY, parseApiError, UNEXPECTED_ERROR_KEY } from '../../core/auth/api-error';
import { ErrorMessageService } from '../../core/auth/error-message.service';
import { safeReturnUrl } from '../../core/auth/return-url';
import { SessionService } from '../../core/auth/session.service';

/** Mirrors LoginRequestValidator.MaxFieldLength on the API. */
const MAX_FIELD_LENGTH = 256;
const LOCKED = 423;
const RATE_LIMITED = 429;

@Component({
  selector: 'cb-login',
  imports: [FormField, TranslocoPipe],
  templateUrl: './login.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Login {
  protected readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly injector = inject(Injector);

  private readonly errorBox = viewChild<ElementRef<HTMLElement>>('errorBox');

  protected readonly model = signal({ userName: '', password: '' });

  // Each message is a translation key (D26), shown through the transloco pipe in the template.
  protected readonly loginForm = form(this.model, (path) => {
    required(path.userName, { message: 'error.auth.user_name_required' });
    maxLength(path.userName, MAX_FIELD_LENGTH, { message: 'error.auth.field_too_long' });
    required(path.password, { message: 'error.auth.password_required' });
    maxLength(path.password, MAX_FIELD_LENGTH, { message: 'error.auth.field_too_long' });
  });

  private readonly failure = signal<ApiError | null>(null);

  protected readonly errorKey = computed(() => {
    const failure = this.failure();
    return failure === null ? null : this.messages.forError(failure);
  });

  /** Lockout and rate limiting: a rough "try again in N minutes" built from Retry-After. */
  protected readonly retryMinutes = computed(() => {
    const failure = this.failure();
    const waits = failure?.status === LOCKED || failure?.status === RATE_LIMITED;
    return waits && failure.retryAfterSeconds !== null ? Math.max(1, Math.ceil(failure.retryAfterSeconds / 60)) : null;
  });

  /** A reference to quote to the administrator, only for failures nobody can explain to the user. */
  protected readonly correlationId = computed(() => {
    const failure = this.failure();
    return failure !== null && (failure.key === UNEXPECTED_ERROR_KEY || failure.key === NETWORK_ERROR_KEY)
      ? failure.correlationId
      : null;
  });

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.session.clearExpiredNotice();

    await submit(this.loginForm, {
      action: async () => {
        this.failure.set(null);
        const { userName, password } = this.model();
        try {
          await firstValueFrom(this.session.login(userName, password));
          await this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
        } catch (error: unknown) {
          this.model.update((value) => ({ ...value, password: '' }));
          this.failure.set(parseApiError(error));
          afterNextRender(() => this.errorBox()?.nativeElement.focus(), { injector: this.injector });
        }
        return undefined;
      },
      onInvalid: () => {
        const field = this.loginForm.userName().invalid() ? this.loginForm.userName : this.loginForm.password;
        field().focusBoundControl();
      },
    });
  }
}
