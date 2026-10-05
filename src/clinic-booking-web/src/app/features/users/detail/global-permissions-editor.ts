import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UserDetail, UsersApi } from '../../../../api/users-api';
import { parseApiError } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { PermissionChecklist } from './permission-checklist';

export interface PermissionsSaved {
  /** The user as the server now has it (reloaded after the save: last write wins, D57). */
  detail: UserDetail;
  /** A translation key for the status message. */
  flash: string;
}

/**
 * The global permissions of one user (D57, D59): checkboxes from GET /api/permissions, saved as a full
 * replace, then the detail is reloaded and the server's state is shown. A refusal such as
 * `error.user.last_administrator` is shown here, in an alert.
 */
@Component({
  selector: 'cb-global-permissions-editor',
  imports: [TranslocoPipe, PermissionChecklist],
  templateUrl: './global-permissions-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GlobalPermissionsEditor {
  private readonly api = inject(UsersApi);
  private readonly messages = inject(ErrorMessageService);
  private readonly injector = inject(Injector);

  readonly user = input.required<UserDetail>();
  /** The assignable global names, from the API. */
  readonly names = input.required<readonly string[]>();
  readonly saved = output<PermissionsSaved>();

  /** Starts from what the server has and follows it whenever the detail is replaced. */
  protected readonly selected = linkedSignal(() => new Set(this.user().globalPermissions));

  protected readonly saving = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);

  private readonly errorBox = viewChild<ElementRef<HTMLElement>>('errorBox');

  protected readonly changed = computed(() => {
    const current = this.selected();
    const stored = this.user().globalPermissions;
    return current.size !== stored.length || stored.some((name) => !current.has(name));
  });

  protected toggle(name: string): void {
    this.selected.update((current) => {
      const next = new Set(current);
      if (!next.delete(name)) {
        next.add(name);
      }
      return next;
    });
  }

  protected async save(): Promise<void> {
    this.errorKey.set(null);
    this.saving.set(true);
    const id = this.user().id;
    const wanted = this.names().filter((name) => this.selected().has(name));

    try {
      const replaced = await firstValueFrom(this.api.replaceGlobalPermissions(id, wanted));
      // Show the server's state: reload it, and fall back to the answer of the PUT if the reload fails.
      const detail = await firstValueFrom(this.api.get(id)).catch(() => replaced);
      this.saved.emit({ detail, flash: 'users.flash.global_saved' });
    } catch (error: unknown) {
      const failure = parseApiError(error);
      this.errorKey.set(this.messages.forError(failure));
      this.correlationId.set(failure.correlationId);
      afterNextRender(() => this.errorBox()?.nativeElement.focus(), { injector: this.injector });
    } finally {
      this.saving.set(false);
    }
  }
}
