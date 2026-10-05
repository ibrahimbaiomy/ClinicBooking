import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkPermissions } from './check-permissions.mjs';

const CS = `
public static class Permissions
{
    public static class Users { public const string Manage = "users.manage"; }
    public static class Specialties { public const string Manage = "specialties.manage"; }
    public static class Doctors { public const string Manage = "doctors.manage"; }
    public static IReadOnlyList<string> Global { get; } = [Users.Manage, Specialties.Manage];
    public static IReadOnlyList<string> ClinicScoped { get; } = [Doctors.Manage];
    public static IReadOnlyList<string> All { get; } = [.. Global, .. ClinicScoped];
}`;
const TS = `
export const Permissions = { UsersManage: 'users.manage', SpecialtiesManage: 'specialties.manage' } as const;
export const ClinicPermissions = { DoctorsManage: 'doctors.manage' } as const;`;

const label = (text) => ({ label: text, description: text });
const LABELS = {
  permissions: {
    users: { manage: label('x') },
    specialties: { manage: label('x') },
    doctors: { manage: label('x') },
  },
};

function run({ ts = TS, cs = CS, labels = { ar: LABELS, en: LABELS }, requireSources = false } = {}) {
  const repo = mkdtempSync(join(tmpdir(), 'perm-'));
  try {
    const web = join(repo, 'src', 'clinic-booking-web');
    mkdirSync(join(web, 'src', 'app', 'core', 'auth'), { recursive: true });
    writeFileSync(join(web, 'src', 'app', 'core', 'auth', 'permissions.ts'), ts);
    mkdirSync(join(web, 'public', 'i18n', 'users'), { recursive: true });
    for (const [language, content] of Object.entries(labels)) {
      if (content !== null) {
        writeFileSync(join(web, 'public', 'i18n', 'users', `${language}.json`), JSON.stringify(content));
      }
    }
    if (cs !== null) {
      mkdirSync(join(repo, 'src', 'ClinicBooking.Domain', 'Permissions'), { recursive: true });
      writeFileSync(join(repo, 'src', 'ClinicBooking.Domain', 'Permissions', 'Permissions.cs'), cs);
    }
    return checkPermissions(web, requireSources);
  } finally {
    rmSync(repo, { recursive: true, force: true });
  }
}

test('matching names in the right lists, with labels, pass', () => {
  assert.deepEqual(run().errors, []);
});

test('a name the API does not define fails', () => {
  const { errors } = run({ ts: TS.replace('users.manage', 'users.managee') });
  assert.ok(errors.some((e) => e.includes('"users.managee"')), errors.join('\n'));
});

test('a global name in ClinicPermissions fails (wrong list)', () => {
  const ts = `${TS.split('\n')[1]}\nexport const ClinicPermissions = { DoctorsManage: 'doctors.manage', UsersManage: 'users.manage' } as const;`;
  const { errors } = run({ ts });
  assert.ok(errors.some((e) => e.includes('"users.manage"') && e.includes('ClinicPermissions') && e.includes('ClinicScoped')), errors.join('\n'));
});

test('a clinic-scoped name in Permissions fails (wrong list)', () => {
  const ts = `export const Permissions = { UsersManage: 'users.manage', DoctorsManage: 'doctors.manage' } as const;\nexport const ClinicPermissions = { DoctorsManage: 'doctors.manage' } as const;`;
  const { errors } = run({ ts });
  assert.ok(errors.some((e) => e.includes('"doctors.manage"') && e.includes('in Permissions') && e.includes('Global')), errors.join('\n'));
});

test('a permission without a label in one language fails, and names the language', () => {
  const noDoctorsInArabic = { permissions: { users: LABELS.permissions.users, specialties: LABELS.permissions.specialties } };
  const { errors } = run({ labels: { ar: noDoctorsInArabic, en: LABELS } });
  assert.ok(errors.some((e) => e.includes('users/ar.json') && e.includes('"doctors.manage"')), errors.join('\n'));
  assert.ok(!errors.some((e) => e.includes('users/en.json')), errors.join('\n'));
});

test('an empty label, or a missing file, fails', () => {
  const emptyLabel = { permissions: { ...LABELS.permissions, users: { manage: { label: '  ', description: 'x' } } } };
  assert.ok(run({ labels: { ar: emptyLabel, en: LABELS } }).errors.some((e) => e.includes('"users.manage"')));
  assert.ok(run({ labels: { ar: LABELS, en: null } }).errors.some((e) => e.includes('en.json does not exist')));
});

test('a permission added only to the C# fails until it has labels', () => {
  const cs = CS.replace('[Doctors.Manage]', '[Doctors.Manage, Doctors.Other]').replace('const string Manage = "doctors.manage";', 'const string Manage = "doctors.manage"; public const string Other = "doctors.other";');
  const { errors } = run({ cs });
  assert.ok(errors.some((e) => e.includes('"doctors.other"') && e.includes('has no label')), errors.join('\n'));
});

test('an empty file or an unreadable C# file fails instead of passing silently', () => {
  assert.ok(run({ ts: 'export const X = 1;' }).errors.length > 0);
  assert.ok(run({ cs: 'class A {}' }).errors.length > 0);
  assert.ok(run({ ts: "export const Permissions = { A: 'users.manage' } as const;" }).errors.length > 0); // no ClinicPermissions
});

test('without the back-end: an error in CI, a warning elsewhere', () => {
  assert.ok(run({ cs: null, requireSources: true }).errors.length > 0);
  const local = run({ cs: null });
  assert.deepEqual(local.errors, []);
  assert.equal(local.warnings.length, 1);
});
