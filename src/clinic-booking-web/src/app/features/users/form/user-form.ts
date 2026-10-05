import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { form, FormField, submit, validate } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UsersApi } from '../../../../api/users-api';
import { ApiError, parseApiError } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { UsersSession } from '../users-session';

/** Mirrors UserRules on the API: 3 to 64 characters, ASCII letters, digits, `.`, `_`, `-`. */
export const USER_NAME_MIN_LENGTH = 3;
export const USER_NAME_MAX_LENGTH = 64;
export const USER_NAME_PATTERN = /^[A-Za-z0-9._-]+$/;
/** Mirrors PasswordErrors.MaxLength: the policy itself is the server's (D48, D57). */
export const PASSWORD_MAX_LENGTH = 128;

type Field = 'userName' | 'temporaryPassword';
const FIELDS: readonly Field[] = ['userName', 'temporaryPassword'];
const USER_NAME_KEY = /^error\.user\.user_name_/;

/**
 * Create a user (D57, D59). The temporary password is typed by the administrator and goes only into
 * the request: the field is emptied after every request, success or failure, and it is never shown
 * again, kept, or put in the URL or router state.
 */
@Component({
  selector: 'cb-user-form',
  imports: [FormField, RouterLink, TranslocoPipe],
  templateUrl: './user-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserForm {
  private readonly api = inject(UsersApi);
  private readonly router = inject(Router);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(UsersSession);
  private readonly injector = inject(Injector);

  protected readonly listQuery = this.memory.lastQuery;

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly errorBox = viewChild<ElementRef<HTMLElement>>('errorBox');

  protected readonly model = signal({ userName: '', temporaryPassword: '' });

  // Messages are the back-end keys (D26); the server stays the authority for the policy.
  protected readonly userForm = form(this.model, (path) => {
    validate(path.userName, ({ value }) => {
      const name = value();
      if (name.trim() === '') return { kind: 'required', message: 'error.user.user_name_required' };
      if (name.length < USER_NAME_MIN_LENGTH) return { kind: 'minLength', message: 'error.user.user_name_too_short' };
      if (name.length > USER_NAME_MAX_LENGTH) return { kind: 'maxLength', message: 'error.user.user_name_too_long' };
      return USER_NAME_PATTERN.test(name) ? undefined : { kind: 'pattern', message: 'error.user.user_name_invalid' };
    });
    validate(path.temporaryPassword, ({ value }) => {
      if (value() === '') return { kind: 'required', message: 'error.password.required' };
      return value().length > PASSWORD_MAX_LENGTH ? { kind: 'maxLength', message: 'error.password.too_long' } : undefined;
    });
  });

  /** Keys the server put on a field; cleared when the user edits that field. */
  protected readonly serverErrors = signal<Record<Field, string[]>>({ userName: [], temporaryPassword: [] });
  protected readonly showPassword = signal(false);
  protected readonly formErrorKey = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());
  }

  protected togglePassword(): void {
    this.showPassword.update((shown) => !shown);
  }

  protected clearServerErrors(field: Field): void {
    if (this.serverErrors()[field].length > 0) {
      this.serverErrors.update((errors) => ({ ...errors, [field]: [] }));
    }
  }

  /** A server message that is not a known key is replaced by error.unexpected (never raw text). */
  protected messageKey(message: string | undefined): string {
    return this.messages.keyFor(message ?? '');
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();

    await submit(this.userForm, {
      action: async () => {
        this.formErrorKey.set(null);
        this.correlationId.set(null);
        this.serverErrors.set({ userName: [], temporaryPassword: [] });

        // Read once, sent once. The password is emptied before the answer comes back; the user name
        // stays so it can be corrected.
        const { userName, temporaryPassword } = this.model();
        this.userForm().reset({ userName, temporaryPassword: '' });
        this.showPassword.set(false);

        let createdId: number | string | null = null;
        let error: ApiError | null = null;
        try {
          const created = await firstValueFrom(this.api.create({ userName, temporaryPassword }));
          createdId = created.id;
        } catch (caught: unknown) {
          error = parseApiError(caught);
        }

        if (error !== null) {
          this.failed(error);
          return undefined;
        }

        this.memory.setFlash('users.flash.created');
        await this.router.navigate(['/users', String(createdId)]);
        return undefined;
      },
      onInvalid: () => {
        const first = FIELDS.find((field) => this.userForm[field]().invalid()) ?? 'userName';
        this.userForm[first]().focusBoundControl();
      },
    });
  }

  /** Puts each server message on its field (400 `errors`, or a key that names its field); the rest is form-level. */
  private failed(error: ApiError): void {
    const placed: Record<Field, string[]> = { userName: [], temporaryPassword: [] };
    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      const field = FIELDS.find((candidate) => candidate === name);
      if (field !== undefined) {
        placed[field].push(...keys);
      }
    }

    if (placed.userName.length === 0 && USER_NAME_KEY.test(error.key)) {
      placed.userName.push(error.key); // 409 error.user.user_name_taken
    }

    this.serverErrors.set(placed);

    const field: Field | null =
      placed.userName.length > 0 ? 'userName' : placed.temporaryPassword.length > 0 ? 'temporaryPassword' : null;
    if (field !== null) {
      afterNextRender(() => this.userForm[field]().focusBoundControl(), { injector: this.injector });
      return;
    }

    this.formErrorKey.set(this.messages.forError(error));
    this.correlationId.set(error.correlationId);
    afterNextRender(() => this.errorBox()?.nativeElement.focus(), { injector: this.injector });
  }
}
