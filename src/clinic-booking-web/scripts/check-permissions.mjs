// Fails when a permission name the UI uses (src/app/core/auth/permissions.ts) is not defined by the
// back end (src/ClinicBooking.Domain/Permissions/Permissions.cs), or sits in the wrong list, and when a
// permission the back end defines has no label in the users scope (public/i18n/users/{ar,en}.json).
// Plain Node, no .NET (D52, D59).
//
// The back end has two lists, `Global` and `ClinicScoped` (D57). The UI has two objects, `Permissions`
// (global) and `ClinicPermissions` (clinic-scoped); a name must be in the matching list. A UI-only typo
// would fail closed (a control stays hidden), so this guards against silent breakage. The label check makes
// the C# the single source of the names: a new permission fails the build until it is described in both
// languages, so the user-management screens never show a bare name by accident.
// Without the back-end sources it warns and skips, unless CI is set, where it fails.

import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const PERMISSION = /^[a-z]+(?:\.[a-z]+)+$/;
const LANGUAGES = ['ar', 'en'];

/** `Users.Manage` -> "users.manage", from the nested constant classes of Permissions.cs. */
function parseConstants(source) {
  const constants = new Map();
  for (const block of source.matchAll(/static\s+class\s+(\w+)\s*\{([^{}]*)\}/g)) {
    for (const constant of block[2].matchAll(/const\s+string\s+(\w+)\s*=\s*"([^"]+)"/g)) {
      constants.set(`${block[1]}.${constant[1]}`, constant[2]);
    }
  }
  return constants;
}

/** The names in `Global` or `ClinicScoped`, resolved through the constants. */
function parseList(source, listName, constants) {
  const match = new RegExp(`${listName}\\s*\\{\\s*get;\\s*\\}\\s*=\\s*\\[([^\\]]*)\\]`).exec(source);
  if (match === null) {
    return null;
  }
  return match[1]
    .split(',')
    .map((item) => item.trim())
    .filter((item) => item !== '')
    .map((item) => constants.get(item) ?? `<unresolved ${item}>`);
}

/** The values of `export const <name> = { A: 'x', ... }`, or null when the object is absent. */
function uiObject(source, name) {
  const match = new RegExp(`export\\s+const\\s+${name}\\s*=\\s*\\{([^}]*)\\}`).exec(source);
  return match === null ? null : [...match[1].matchAll(/:\s*'([^']+)'/g)].map((m) => m[1]);
}

/** `users.manage` -> permissions.users.manage.label in a parsed users-scope file. */
function labelOf(translations, name) {
  let node = translations?.permissions;
  for (const part of name.split('.')) {
    node = node?.[part];
  }
  return typeof node?.label === 'string' && node.label.trim() !== '' ? node.label : null;
}

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

  const csharp = readFileSync(backend, 'utf8');
  const constants = parseConstants(csharp);
  const global = parseList(csharp, 'Global', constants);
  const clinicScoped = parseList(csharp, 'ClinicScoped', constants);
  if (global === null || global.length === 0 || clinicScoped === null || clinicScoped.length === 0) {
    return { errors: ['no Global or ClinicScoped permission list found in Permissions.cs; the check needs updating'], warnings: [] };
  }
  for (const name of [...global, ...clinicScoped]) {
    if (name.startsWith('<unresolved')) {
      errors.push(`Permissions.cs: ${name} could not be resolved to a constant; the check needs updating`);
    }
  }

  const source = readFileSync(ui, 'utf8');
  const uiGlobal = uiObject(source, 'Permissions');
  const uiScoped = uiObject(source, 'ClinicPermissions');
  if (uiGlobal === null || uiGlobal.length === 0 || uiScoped === null || uiScoped.length === 0) {
    return { errors: ['permissions.ts must export non-empty `Permissions` and `ClinicPermissions` objects'], warnings: [] };
  }

  const verify = (names, expected, other, objectName, listName) => {
    for (const name of names) {
      if (!PERMISSION.test(name) || (!expected.includes(name) && !other.includes(name))) {
        errors.push(`permissions.ts: "${name}" is not a permission the API defines (Permissions.cs)`);
      } else if (!expected.includes(name)) {
        errors.push(`permissions.ts: "${name}" is in ${objectName} but Permissions.cs lists it in the other list, not ${listName}`);
      }
    }
  };
  verify(uiGlobal, global, clinicScoped, 'Permissions', 'Global');
  verify(uiScoped, clinicScoped, global, 'ClinicPermissions', 'ClinicScoped');

  // Every permission the API defines has a label in both languages (the users scope).
  for (const language of LANGUAGES) {
    const file = join(root, 'public', 'i18n', 'users', `${language}.json`);
    if (!existsSync(file)) {
      errors.push(`${file} does not exist: the permission labels live there`);
      continue;
    }
    let translations;
    try {
      translations = JSON.parse(readFileSync(file, 'utf8'));
    } catch {
      errors.push(`${file} is not valid JSON`);
      continue;
    }
    for (const name of [...global, ...clinicScoped]) {
      if (labelOf(translations, name) === null) {
        errors.push(`users/${language}.json: permission "${name}" has no label (permissions.${name}.label)`);
      }
    }
  }

  return { errors, warnings: [] };
}

function main() {
  const { errors, warnings } = checkPermissions(process.cwd(), Boolean(process.env.CI));
  warnings.forEach((w) => console.warn(`  warning: ${w}`));
  if (errors.length === 0) {
    console.log('check:permissions OK: every permission name in the UI exists in the right API list, and every API permission has a label.');
    return;
  }
  console.error('check:permissions FAILED:');
  errors.forEach((e) => console.error(`  ${e}`));
  process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
