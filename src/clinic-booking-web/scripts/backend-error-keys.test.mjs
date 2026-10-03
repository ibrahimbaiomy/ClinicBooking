import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkI18n } from './check-i18n.mjs';
import { extractCSharpStrings } from './backend-error-keys.mjs';

const values = (source) => extractCSharpStrings(source).map((s) => s.value);

test('the scanner skips comments and keeps strings that contain comment markers', () => {
  const source = `
    // "error.in.line_comment"
    /// <summary>"error.in.doc_comment"</summary>
    /* "error.in.block_comment" */
    var url = "http://example.com/error.in.string_with_slashes";
    var key = "error.real.key"; // trailing "error.in.trailing_comment"
  `;
  assert.deepEqual(values(source), ['http://example.com/error.in.string_with_slashes', 'error.real.key']);
});

test('the scanner reads verbatim, escaped, raw and interpolated strings and char literals', () => {
  const source = String.raw`
    var a = @"error.a.verbatim ""quoted""";
    var b = "error.b.escaped \" still inside";
    var c = '"'; var d = "error.d.after_char";
    var e = """error.e.raw""";
    var f = $"error.f.{x}";
  `;
  assert.deepEqual(values(source), ['error.a.verbatim "quoted"', 'error.b.escaped " still inside', 'error.d.after_char', 'error.e.raw', 'error.f.{x}']);
});

test('the scanner reports the line a string starts on', () => {
  const found = extractCSharpStrings('a\n\n/* x\ny */ var k = "error.x.y";');
  assert.equal(found[0].line, 4);
});

const AR = { error: { a: { one: 'واحد' }, unexpected: 'غير متوقع', http: { '400': 'خطأ' } } };
const EN = { error: { a: { one: 'one' }, unexpected: 'unexpected', http: { '400': 'bad' } } };

/** Builds a temp repository: web project at src/clinic-booking-web, C# under src/ClinicBooking.*. */
function run(csharp, { ar = AR, en = EN, config = { dynamicSources: [] }, requireSources = true, noSources = false } = {}) {
  const repo = mkdtempSync(join(tmpdir(), 'backend-keys-'));
  try {
    const web = join(repo, 'src', 'clinic-booking-web');
    mkdirSync(join(web, 'public', 'i18n'), { recursive: true });
    writeFileSync(join(web, 'public', 'i18n', 'ar.json'), JSON.stringify(ar));
    writeFileSync(join(web, 'public', 'i18n', 'en.json'), JSON.stringify(en));

    if (!noSources) {
      for (const [name, content] of Object.entries(csharp)) {
        const path = join(repo, name);
        mkdirSync(join(path, '..'), { recursive: true });
        writeFileSync(path, content);
      }
    }

    return checkI18n(web, { backend: { sourceDirectory: join(repo, 'src'), repositoryRoot: repo, config, requireSources } });
  } finally {
    rmSync(repo, { recursive: true, force: true });
  }
}

const KEYS = 'src/ClinicBooking.Application/Keys.cs';

test('covered back-end keys pass', () => {
  const { errors } = run({ [KEYS]: 'const string A = "error.a.one"; const string U = "error.unexpected";' });
  assert.deepEqual(errors, []);
});

test('a back-end key without a translation fails in each language, naming the C# file and line', () => {
  const { errors } = run({ [KEYS]: 'class K {\n const string A = "error.a.two";\n}' });
  assert.ok(errors.some((e) => e.includes('src/ClinicBooking.Application/Keys.cs:2') && e.includes('"error.a.two"') && e.includes('ar.json')), errors.join('\n'));
  assert.ok(errors.some((e) => e.includes('"error.a.two"') && e.includes('en.json')), errors.join('\n'));
});

test('a key that is only in a comment, in tests/, in bin/ or in obj/ is not required', () => {
  const { errors } = run({
    [KEYS]: '// "error.in.comment"\nconst string A = "error.a.one";',
    'tests/ClinicBooking.Tests/T.cs': 'var k = "error.in.tests";',
    'src/ClinicBooking.Api/bin/Debug/X.cs': 'var k = "error.in.bin";',
    'src/ClinicBooking.Api/obj/X.cs': 'var k = "error.in.obj";',
    'src/ClinicBooking.Api/Migrations/M.cs': 'var k = "error.in.migrations";',
  });
  assert.deepEqual(errors, []);
});

test('a prefix literal in an undeclared file fails: the keys it builds cannot be read', () => {
  const { errors } = run({ 'src/ClinicBooking.Api/P.cs': 'const string Prefix = "error.";' });
  assert.ok(errors.some((e) => e.includes('P.cs:1') && e.includes('backend-error-keys.json')), errors.join('\n'));
});

test('a declared dynamic source contributes its keys, which must be translated', () => {
  const file = 'src/ClinicBooking.Api/P.cs';
  const covered = { dynamicSources: [{ file, keys: ['error.http.400'] }] };
  assert.deepEqual(run({ [file]: 'const string Prefix = "error.";' }, { config: covered }).errors, []);

  const missing = { dynamicSources: [{ file, keys: ['error.http.400', 'error.http.404'] }] };
  const { errors } = run({ [file]: 'const string Prefix = "error.";' }, { config: missing });
  assert.ok(errors.some((e) => e.includes('"error.http.404"') && e.includes('en.json')), errors.join('\n'));
});

test('a declared dynamic source that no longer exists or no longer uses a prefix is stale', () => {
  const stale = { dynamicSources: [{ file: 'src/ClinicBooking.Api/Gone.cs', keys: [] }] };
  const { errors } = run({ [KEYS]: 'const string A = "error.a.one";' }, { config: stale });
  assert.ok(errors.some((e) => e.includes('Gone.cs') && e.includes('stale')), errors.join('\n'));
});

test('a malformed declared key fails', () => {
  const file = 'src/ClinicBooking.Api/P.cs';
  const { errors } = run({ [file]: 'const string Prefix = "error.";' }, { config: { dynamicSources: [{ file, keys: ['Error.HTTP.400'] }] } });
  assert.ok(errors.some((e) => e.includes('not a well-formed error key')), errors.join('\n'));
});

test('without back-end sources: an error in CI, a warning elsewhere', () => {
  const inCi = run({}, { noSources: true, requireSources: true });
  assert.ok(inCi.errors.some((e) => e.includes('back-end sources not found')), inCi.errors.join('\n'));

  const local = run({}, { noSources: true, requireSources: false });
  assert.deepEqual(local.errors, []);
  assert.ok(local.warnings.some((w) => w.includes('back-end sources not found')));
});

test('the back-end scan is off unless asked for', () => {
  const repo = mkdtempSync(join(tmpdir(), 'backend-keys-'));
  try {
    mkdirSync(join(repo, 'public', 'i18n'), { recursive: true });
    writeFileSync(join(repo, 'public', 'i18n', 'ar.json'), JSON.stringify(AR));
    writeFileSync(join(repo, 'public', 'i18n', 'en.json'), JSON.stringify(EN));
    assert.deepEqual(checkI18n(repo).errors, []);
  } finally {
    rmSync(repo, { recursive: true, force: true });
  }
});
