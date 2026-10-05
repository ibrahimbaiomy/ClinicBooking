import { inject, Injectable } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';

export interface PermissionLabel {
  label: string;
  description: string;
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

/**
 * Human-readable names for permissions (D59). The labels live in one translation object,
 * `users.permissions.<area>.<action>.{label,description}`, read as a whole, so no key is built at
 * runtime and the list of permissions is never written down twice: the API sends the names
 * (GET /api/permissions) and `check:permissions` fails the build when the C# has a name without a label.
 * A name without a label (a stale client) has none here: callers show the bare name.
 */
@Injectable({ providedIn: 'root' })
export class PermissionLabels {
  private readonly transloco = inject(TranslocoService);

  // Emits again after every language change, once the scope file for it has loaded.
  private readonly tree = toSignal(this.transloco.selectTranslateObject<unknown>('users.permissions'), {
    initialValue: null,
  });

  /** The label and description of a permission name in the active language, or null when it has none. */
  labelOf(name: string): PermissionLabel | null {
    let node: unknown = this.tree();
    for (const part of name.split('.')) {
      node = isRecord(node) ? node[part] : undefined;
    }

    if (isRecord(node) && typeof node['label'] === 'string' && node['label'] !== '') {
      return { label: node['label'], description: typeof node['description'] === 'string' ? node['description'] : '' };
    }
    return null;
  }
}
