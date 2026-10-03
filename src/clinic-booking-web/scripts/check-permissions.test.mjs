import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkPermissions } from './check-permissions.mjs';

const CS = 'public const string Manage = "users.manage"; public const string Other = "specialties.manage";';
const TS = "export const Permissions = { UsersManage: 'users.manage', SpecialtiesManage: 'specialties.manage' } as const;";

function run(ts, cs, requireSources = false) {
  const repo = mkdtempSync(join(tmpdir(), 'perm-'));
  try {
    const web = join(repo, 'src', 'clinic-booking-web');
    mkdirSync(join(web, 'src', 'app', 'core', 'auth'), { recursive: true });
    writeFileSync(join(web, 'src', 'app', 'core', 'auth', 'permissions.ts'), ts);
    if (cs !== null) {
      mkdirSync(join(repo, 'src', 'ClinicBooking.Domain', 'Permissions'), { recursive: true });
      writeFileSync(join(repo, 'src', 'ClinicBooking.Domain', 'Permissions', 'Permissions.cs'), cs);
    }
    return checkPermissions(web, requireSources);
  } finally {
    rmSync(repo, { recursive: true, force: true });
  }
}

test('matching names pass', () => {
  assert.deepEqual(run(TS, CS).errors, []);
});

test('a name the API does not define fails', () => {
  const { errors } = run(TS.replace('users.manage', 'users.managee'), CS);
  assert.ok(errors.some((e) => e.includes('"users.managee"')), errors.join('\n'));
});

test('an empty file or an unreadable C# file fails instead of passing silently', () => {
  assert.ok(run('export const X = 1;', CS).errors.length > 0);
  assert.ok(run(TS, 'class A {}').errors.length > 0);
});

test('without the back-end: an error in CI, a warning elsewhere', () => {
  assert.ok(run(TS, null, true).errors.length > 0);
  const local = run(TS, null, false);
  assert.deepEqual(local.errors, []);
  assert.equal(local.warnings.length, 1);
});
