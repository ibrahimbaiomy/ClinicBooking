import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { findViolations } from './check-logical-properties.mjs';

function check(files) {
  const root = mkdtempSync(join(tmpdir(), 'logical-'));
  try {
    for (const [name, content] of Object.entries(files)) {
      const path = join(root, name);
      mkdirSync(join(path, '..'), { recursive: true });
      writeFileSync(path, content);
    }
    return findViolations(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test('logical utilities pass', () => {
  const html = '<div class="ms-2 me-2 ps-4 pe-4 start-0 end-0 text-start text-end rounded-s-lg rounded-e border-s border-e-2 rounded-lg p-2 mx-auto"></div>';
  assert.deepEqual(check({ 'a.html': html }), []);
});

test('physical utilities are reported with file, line and replacement', () => {
  const found = check({ 'app/a.html': '<p>ok</p>\n<div class="p-2 ml-4 text-sm"></div>' });
  assert.equal(found.length, 1);
  assert.equal(found[0].line, 2);
  assert.equal(found[0].found, 'ml-4');
  assert.equal(found[0].use, 'ms-');
});

for (const cls of [
  'ml-2', 'mr-2', 'pl-2', 'pr-2', 'left-0', 'right-0', 'text-left', 'text-right',
  'rounded-l', 'rounded-r-lg', 'rounded-tl-md', 'border-l', 'border-r-2', 'float-left',
  '-ml-2', '!pr-1', 'md:ml-2', 'rtl:pr-3', 'hover:text-left', 'md:hover:-mr-1', 'ml-[2px]', '[&>*]:pl-1',
]) {
  test(`reports ${cls}`, () => {
    assert.equal(check({ 'a.html': `<div class="x ${cls} y"></div>` }).length, 1);
  });
}

test('words that merely start like a utility are not reported', () => {
  const html = '<input placeholder="x" class="rounded-lg border-2 p-1 left-aligned-text"> copyright-notice';
  assert.deepEqual(check({ 'a.html': html }), []);
});

test('classes in a component string are checked', () => {
  const found = check({ 'a.ts': "const cls = 'flex ml-2';" });
  assert.equal(found.length, 1);
});

test('physical CSS properties are reported in stylesheets', () => {
  const css = '.a { margin-left: 1px; }\n.b { padding-right: 2px; }\n.c { text-align: left; }\n.d { left: 0; }\n.e { margin-inline-start: 1px; }';
  const found = check({ 'a.css': css });
  assert.deepEqual(found.map((v) => v.line), [1, 2, 3, 4]);
});

test('the logical-ok marker skips a line', () => {
  assert.deepEqual(check({ 'a.html': '<div class="ml-2"></div> <!-- logical-ok: third-party widget -->' }), []);
});

test('spec files are not checked', () => {
  assert.deepEqual(check({ 'a.spec.ts': "expect(el.className).toBe('ml-2');" }), []);
});
