import { inject, Injectable, OnDestroy, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UserDetail, UsersApi } from '../../../../api/users-api';
import { MESSAGE_DURATION_MS } from '../../../app-settings';
import { parseApiError } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';

/** Where a permission lives: the global set, or one clinic (its id). */
export type PermissionTarget = 'global' | `clinic:${string}`;

export const clinicTarget = (clinicId: number | string): PermissionTarget => `clinic:${clinicId}`;

/** One checkbox: a permission name in a target. */
export const itemKey = (target: PermissionTarget, name: string): string => `${target}|${name}`;

interface Job {
  target: PermissionTarget;
  name: string;
  /** The assignable names of the target, in the API's order: the request lists the selection in it. */
  order: readonly string[];
}

/**
 * Permissions save on each change (D64), for one user page. Every change sends the target's full set (the
 * API is a full replace, D57), one request at a time in the order of the changes; the set is read when the
 * request leaves, so a later change never undoes an earlier one. A checkbox is pending (disabled) from its
 * change until its request answers. On success the server's answer becomes the state and the item shows
 * "Saved" for the configured duration; on failure that item returns to the server's state and its
 * translated error shows next to it. Errors are never toasts.
 */
@Injectable()
export class PermissionSaveQueue implements OnDestroy {
  private readonly api = inject(UsersApi);
  private readonly messages = inject(ErrorMessageService);
  private readonly duration = inject(MESSAGE_DURATION_MS);

  /** The user as the server last answered. */
  readonly server = signal<UserDetail | null>(null);
  /** Item keys whose request is queued or in flight. */
  readonly pending = signal<ReadonlySet<string>>(new Set());
  /** Item keys that saved recently ("Saved" indicator). */
  readonly saved = signal<ReadonlySet<string>>(new Set());
  /** Translated error keys, by item key. */
  readonly errors = signal<Readonly<Record<string, string>>>({});

  /** Called after each successful save with the server's answer (the page keeps the session honest). */
  afterSave: ((detail: UserDetail) => Promise<void>) | null = null;

  /** Selections that wait for the server, by target; absent means "as the server has it". */
  private readonly desired = signal<Readonly<Record<string, ReadonlySet<string>>>>({});
  private readonly jobs: Job[] = [];
  private running = false;
  private readonly savedTimers = new Map<string, ReturnType<typeof setTimeout>>();

  ngOnDestroy(): void {
    this.savedTimers.forEach((timer) => clearTimeout(timer));
    this.jobs.length = 0;
  }

  /** What the server has for a target. */
  serverSelection(target: PermissionTarget): ReadonlySet<string> {
    const user = this.server();
    if (user === null) return new Set();
    if (target === 'global') return new Set(user.globalPermissions);
    const clinicId = target.slice('clinic:'.length);
    return new Set(user.clinicPermissions.find((entry) => String(entry.clinicId) === clinicId)?.permissions ?? []);
  }

  /** What the checkboxes show: the change waiting for the server, else the server's state. */
  selection(target: PermissionTarget): ReadonlySet<string> {
    return this.desired()[target] ?? this.serverSelection(target);
  }

  /** Whether any change is waiting or in flight for the target. */
  isBusy(target: PermissionTarget): boolean {
    return [...this.pending()].some((key) => key.startsWith(`${target}|`));
  }

  change(target: PermissionTarget, name: string, checked: boolean, order: readonly string[]): void {
    const key = itemKey(target, name);
    const next = new Set(this.selection(target));
    if (checked) next.add(name);
    else next.delete(name);

    this.desired.update((all) => ({ ...all, [target]: next }));
    this.errors.update((all) => without(all, key));
    this.unmarkSaved(key);
    this.pending.update((keys) => new Set(keys).add(key));
    this.jobs.push({ target, name, order });
    void this.run();
  }

  private async run(): Promise<void> {
    if (this.running) return;
    this.running = true;
    try {
      for (let job = this.jobs.shift(); job !== undefined; job = this.jobs.shift()) {
        await this.send(job);
      }
    } finally {
      this.running = false;
    }
  }

  private async send(job: Job): Promise<void> {
    const key = itemKey(job.target, job.name);
    const user = this.server();
    if (user === null) return;
    // Read the set when the request leaves: every earlier change is already in it.
    const wanted = job.order.filter((name) => this.selection(job.target).has(name));

    try {
      const detail = await firstValueFrom(
        job.target === 'global'
          ? this.api.replaceGlobalPermissions(user.id, wanted)
          : this.api.replaceClinicPermissions(user.id, job.target.slice('clinic:'.length), wanted),
      );
      this.server.set(detail);
      this.settle(job.target);
      this.markSaved(key);
      this.donePending(key);
      await this.afterSave?.(detail);
    } catch (error: unknown) {
      // Back to the server's state for this item only; later changes of the same target stay queued.
      const serverHas = this.serverSelection(job.target).has(job.name);
      this.desired.update((all) => {
        const current = new Set(all[job.target] ?? this.serverSelection(job.target));
        if (serverHas) current.add(job.name);
        else current.delete(job.name);
        return { ...all, [job.target]: current };
      });
      this.settle(job.target);
      this.errors.update((all) => ({ ...all, [key]: this.messages.forError(parseApiError(error)) }));
      this.donePending(key);
    }
  }

  /** With nothing more queued for the target, the server's answer is the state again. */
  private settle(target: PermissionTarget): void {
    if (!this.jobs.some((job) => job.target === target)) {
      this.desired.update((all) => without(all, target));
    }
  }

  private donePending(key: string): void {
    if (this.jobs.some((job) => itemKey(job.target, job.name) === key)) return;
    this.pending.update((keys) => {
      const next = new Set(keys);
      next.delete(key);
      return next;
    });
  }

  private markSaved(key: string): void {
    this.unmarkSaved(key);
    this.saved.update((keys) => new Set(keys).add(key));
    this.savedTimers.set(
      key,
      setTimeout(() => this.unmarkSaved(key), this.duration),
    );
  }

  private unmarkSaved(key: string): void {
    const timer = this.savedTimers.get(key);
    if (timer !== undefined) clearTimeout(timer);
    this.savedTimers.delete(key);
    if (this.saved().has(key)) {
      this.saved.update((keys) => {
        const next = new Set(keys);
        next.delete(key);
        return next;
      });
    }
  }
}

function without<T>(record: Readonly<Record<string, T>>, key: string): Record<string, T> {
  const copy = { ...record };
  delete copy[key];
  return copy;
}
