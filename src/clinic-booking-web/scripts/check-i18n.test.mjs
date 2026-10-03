import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkI18n } from './check-i18n.mjs';

const AR = { app: { title: 'حجز العيادات' }, hello: 'مرحبا {{ name }}', language: { ar: 'العربية' } };
const EN = { app: { title: 'ClinicBooking' }, hello: 'Hello {{ name }}', language: { ar: 'Arabic' } };

function run(files) {
  const root = mkdtempSync(join(tmpdir(), 'i18n-'));
  try {
    const all = { 'public/i18n/ar.json': JSON.stringify(AR), 'public/i18n/en.json': JSON.stringify(EN), ...files };
    for (const [name, content] of Object.entries(all)) {
      if (content === null) {
        continue;
      }
      const path = join(root, name);
      mkdirSync(join(path, '..'), { recursive: true });
      writeFileSync(path, typeof content === 'string' ? content : JSON.stringify(content));
    }
    return checkI18n(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

const GOOD_TEMPLATE = `
<header>
  <h1>{{ 'app.title' | transloco }}</h1>
  @if (language() === 'ar') {
    <button [attr.aria-label]="'language.ar' | transloco">{{ 'language.ar' | transloco }}</button>
  } @else {
    <span>{{ 'hello' | transloco: { name: user() } }}</span>
  }
  <!-- a comment with words -->
</header>`;

test('a clean project passes', () => {
  const { errors } = run({ 'src/app/a.html': GOOD_TEMPLATE });
  assert.deepEqual(errors, []);
});

test('index.html and spec files are not scanned for literal text', () => {
  const { errors } = run({ 'src/index.html': '<title>ClinicBooking</title>', 'src/a.spec.ts': "x.translate('nope.key')" });
  assert.deepEqual(errors, []);
});

test('a key present in one language only fails', () => {
  const { errors } = run({ 'public/i18n/en.json': { app: { title: 'x' }, hello: 'Hello {{ name }}' } });
  assert.ok(errors.some((e) => e.includes('en.json is missing "language.ar"')), errors.join('\n'));
});

test('different placeholders fail', () => {
  const { errors } = run({ 'public/i18n/en.json': { ...EN, hello: 'Hello {{ who }}' } });
  assert.ok(errors.some((e) => e.includes('different {{placeholders}}')), errors.join('\n'));
});

test('empty and non-string values fail', () => {
  const { errors } = run({
    'public/i18n/ar.json': { ...AR, app: { title: '  ' }, n: 3 },
    'public/i18n/en.json': { ...EN, n: 3 },
  });
  assert.ok(errors.some((e) => e.includes('"app.title" is empty')));
  assert.ok(errors.some((e) => e.includes('"n" must be a string')));
});

test('a missing language file fails', () => {
  const { errors } = run({ 'public/i18n/en.json': null });
  assert.ok(errors.some((e) => e.includes('en.json is missing')));
});

test('a used key that does not exist fails with file and line', () => {
  const { errors } = run({ 'src/app/a.html': "<p>{{ 'nope.key' | transloco }}</p>" });
  assert.ok(errors.some((e) => e.startsWith('src/app/a.html:1:') && e.includes('"nope.key" is missing from ar.json')));
  assert.ok(errors.some((e) => e.includes('"nope.key" is missing from en.json')));
});

test('keys used through the structural directive and in code are verified', () => {
  const html = `<ng-container *transloco="let t"><p>{{ t('app.title') }}</p><p>{{ t('bad.one') }}</p></ng-container>`;
  const ts = "export class A { x = this.transloco.translate('bad.two'); y = this.transloco.selectTranslate('app.title'); }";
  const { errors } = run({ 'src/app/a.html': html, 'src/app/a.ts': ts });
  assert.ok(errors.some((e) => e.includes('"bad.one"')));
  assert.ok(errors.some((e) => e.includes('"bad.two"')));
  assert.ok(!errors.some((e) => e.includes('"app.title"')));
});

test('literal text in a template fails (Latin and Arabic)', () => {
  const { errors } = run({ 'src/app/a.html': '<p>Hello world</p>\n<p>مرحبا بكم</p>' });
  assert.ok(errors.some((e) => e.startsWith('src/app/a.html:1:') && e.includes('literal text')));
  assert.ok(errors.some((e) => e.startsWith('src/app/a.html:2:') && e.includes('literal text')));
});

test('literal user-facing attributes fail, bound keys pass', () => {
  const { errors } = run({
    'src/app/a.html': `<img alt="A photo" src="x.png"><input placeholder="Search"><button [attr.aria-label]="'app.title' | transloco"></button><a [title]="'Plain'"></a>`,
  });
  assert.equal(errors.filter((e) => e.includes('literal alt')).length, 1);
  assert.equal(errors.filter((e) => e.includes('literal placeholder')).length, 1);
  assert.equal(errors.filter((e) => e.includes('binds a literal string')).length, 1);
});

test('an interpolation with a literal string fails', () => {
  const { errors } = run({ 'src/app/a.html': "<p>{{ 'Plain text' }}</p>" });
  assert.ok(errors.some((e) => e.includes('interpolation contains a literal string')));
});

test('control flow with quoted conditions is not treated as text', () => {
  const html = `@if (items().length > 0 && mode() === 'edit') { <p>{{ 'app.title' | transloco }}</p> } @for (item of items(); track item.id) { <i>{{ item.id }}</i> } @empty { <p>{{ 'app.title' | transloco }}</p> }`;
  assert.deepEqual(run({ 'src/app/a.html': html }).errors, []);
});

test('a dynamic key needs the marker comment, and the marker keys are verified', () => {
  const dynamic = '<p>{{ key() | transloco }}</p>';
  assert.ok(run({ 'src/app/a.html': dynamic }).errors.some((e) => e.includes('not a string literal')));

  const marked = '<p>{{ key() | transloco }}</p> <!-- i18n-keys: app.title, language.ar -->';
  assert.deepEqual(run({ 'src/app/a.html': marked }).errors, []);

  const badMarker = '<p>{{ key() | transloco }}</p> <!-- i18n-keys: app.title, not.there -->';
  assert.ok(run({ 'src/app/a.html': badMarker }).errors.some((e) => e.includes('"not.there"')));

  const tsDynamic = 'const t = this.transloco.translate(`status.${s}`);';
  assert.ok(run({ 'src/app/a.ts': tsDynamic }).errors.some((e) => e.includes('not a string literal')));
  const tsMarked = '// i18n-keys: app.title\nconst t = this.transloco.translate(`status.${s}`);';
  assert.deepEqual(run({ 'src/app/a.ts': tsMarked }).errors, []);
});

test('inline templates fail', () => {
  const ts = "@Component({ selector: 'cb-x', template: '<p>hi</p>' }) export class X {}";
  assert.ok(run({ 'src/app/x.ts': ts }).errors.some((e) => e.includes('inline template')));
  const ok = "@Component({ selector: 'cb-x', templateUrl: './x.html' }) export class X {}";
  assert.deepEqual(run({ 'src/app/x.ts': ok }).errors, []);
});

test('scope folders: parity is checked and scoped keys resolve', () => {
  const files = {
    'public/i18n/specialties/ar.json': { title: 'التخصصات' },
    'public/i18n/specialties/en.json': { title: 'Specialties' },
    'src/app/a.html': "<h2>{{ 'specialties.title' | transloco }}</h2>",
  };
  assert.deepEqual(run(files).errors, []);

  const broken = { ...files, 'public/i18n/specialties/en.json': {} };
  assert.ok(run(broken).errors.some((e) => e.includes('missing "title"')));
});

test('unused keys are warnings only, and error.* keys are exempt', () => {
  const { errors, warnings } = run({
    'public/i18n/ar.json': { ...AR, error: { auth: { unauthorized: 'غير مصرح' } } },
    'public/i18n/en.json': { ...EN, error: { auth: { unauthorized: 'Unauthorized' } } },
  });
  assert.deepEqual(errors, []);
  assert.ok(warnings.some((w) => w.includes('"hello"')));
  assert.ok(!warnings.some((w) => w.includes('error.')));
});
