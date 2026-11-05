import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { Permissions } from '../../../core/auth/permissions';
import { ConfirmDialog } from '../../../shared/ui/confirm-dialog';
import { itemKey, PermissionSaveQueue } from './permission-save-queue';
import { PermissionChecklist, PermissionToggle } from './permission-checklist';

const TARGET = 'global';

/**
 * The global permissions of one user (D57, D59), saved on each change (D64): no Save button. Removing
 * users.manage from your own account asks first. The protected administrator's users.manage is locked
 * (the API refuses it too, error.user.protected_keeps_users_manage).
 */
@Component({
  selector: 'cb-global-permissions-editor',
  imports: [TranslocoPipe, PermissionChecklist, ConfirmDialog],
  templateUrl: './global-permissions-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GlobalPermissionsEditor {
  protected readonly queue = inject(PermissionSaveQueue);

  /** The assignable global names, from the API. */
  readonly names = input.required<readonly string[]>();
  /** The page shows the signed-in administrator's own account. */
  readonly isOwn = input(false);

  protected readonly selected = computed(() => this.queue.selection(TARGET));
  protected readonly empty = computed(() => this.queue.serverSelection(TARGET).size === 0);

  protected readonly pending = computed(() => this.byName(this.queue.pending()));
  protected readonly saved = computed(() => this.byName(this.queue.saved()));
  protected readonly errors = computed(() => {
    const all = this.queue.errors();
    return Object.fromEntries(this.names().flatMap((name) => (all[itemKey(TARGET, name)] ? [[name, all[itemKey(TARGET, name)]]] : [])));
  });

  /** A protected user keeps users.manage once held; granting it stays possible. */
  protected readonly locked = computed(() => {
    const user = this.queue.server();
    const locked: Record<string, string> = {};
    if (user?.isProtected === true && user.globalPermissions.includes(Permissions.UsersManage)) {
      locked[Permissions.UsersManage] = 'users.global.locked';
    }
    return locked;
  });

  protected readonly confirmingRemoval = signal(false);

  protected onToggled(change: PermissionToggle): void {
    if (this.isOwn() && change.name === Permissions.UsersManage && !change.checked) {
      // Nothing changes until confirmed: the box shows the current state again.
      change.input.checked = true;
      this.confirmingRemoval.set(true);
      return;
    }
    this.queue.change(TARGET, change.name, change.checked, this.names());
  }

  protected confirmRemoval(): void {
    this.confirmingRemoval.set(false);
    this.queue.change(TARGET, Permissions.UsersManage, false, this.names());
  }

  protected cancelRemoval(): void {
    this.confirmingRemoval.set(false);
  }

  private byName(keys: ReadonlySet<string>): ReadonlySet<string> {
    return new Set(this.names().filter((name) => keys.has(itemKey(TARGET, name))));
  }
}
