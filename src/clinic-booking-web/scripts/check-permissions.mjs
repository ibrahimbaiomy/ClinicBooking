// Fails when a permission name the UI uses (src/app/core/auth/permissions.ts) is not defined by the
// back end (src/ClinicBooking.Domain/Permissions/Permissions.cs). Plain Node, no .NET (D52).
// A UI-only typo would fail closed (a control stays hidden), so this guards against silent breakage.
// Without the back-end sources it warns and skips, unless CI is set, where it fails.

import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const PERMISSION = /^[a-z]+(?:\.[a-z]+)+$/;

const constants = (source) => [...source.matchAll(/const\s+string\s+\w+\s*=\s*"([^"]+)"/g)].map((m) => m[1]);
const uiNames = (source) => [...source.matchAll(/:\s*'([^']+)'/g)].map((m) => m[1]);

/** @returns {{ errors: string[], warnings: string[] }} */
export function checkPermissions(root, requireSources = false) {
  const repository = join(root, '..', '..');
  const backend = join(repository, 'src', 'ClinicBooking.Domain', 'Permissions', 'Permissions.cs');
  const ui = join(root, 'src', 'app', 'core', 'auth', 'permissions.ts');
  const errors = [];

  if (!existsSync(ui)) {
    return { errors: [`${ui} does not exist`], warnings: [] };
  }
  if (!existsSync(backend)) {
    const message = 'back-end Permissions.cs not found; permission names were not checked';
    return requireSources ? { errors: [`${message} (CI must run this check where the sources exist)`], warnings: [] } : { errors: [], warnings: [message] };
  }

  const defined = new Set(constants(readFileSync(backend, 'utf8')));
  if (defined.size === 0) {
    errors.push('no permission constants found in Permissions.cs; the check needs updating');
  }

  const used = uiNames(readFileSync(ui, 'utf8'));
  if (used.length === 0) {
    errors.push('no permission names found in permissions.ts; the check needs updating');
  }
  for (const name of used) {
    if (!PERMISSION.test(name) || !defined.has(name)) {
      errors.push(`permissions.ts: "${name}" is not a permission the API defines (Permissions.cs)`);
    }
  }
  return { errors, warnings: [] };
}

function main() {
  const { errors, warnings } = checkPermissions(process.cwd(), Boolean(process.env.CI));
  warnings.forEach((w) => console.warn(`  warning: ${w}`));
  if (errors.length === 0) {
    console.log('check:permissions OK: every permission name in the UI exists in the API.');
    return;
  }
  console.error('check:permissions FAILED:');
  errors.forEach((e) => console.error(`  ${e}`));
  process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
