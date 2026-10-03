import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkSchema, generateSchemaText } from './gen-api.mjs';

const DOCUMENT = {
  openapi: '3.1.1',
  info: { title: 'Test', version: '1' },
  paths: { '/api/ping': { get: { responses: { 200: { description: 'OK' } } } } },
};

function withFiles(callback) {
  const directory = mkdtempSync(join(tmpdir(), 'gen-api-test-'));
  try {
    const openapiPath = join(directory, 'openapi.json');
    writeFileSync(openapiPath, JSON.stringify(DOCUMENT));
    return callback({ openapiPath, schemaPath: join(directory, 'schema.d.ts') });
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test('generation is deterministic and uses LF endings', () => {
  withFiles(({ openapiPath }) => {
    const first = generateSchemaText(openapiPath);
    const second = generateSchemaText(openapiPath);
    assert.equal(first, second);
    assert.ok(!first.includes('\r'));
    assert.ok(first.endsWith('\n'));
    assert.ok(first.includes('"/api/ping"'));
  });
});

test('check passes when schema.d.ts matches, even with CRLF endings', () => {
  withFiles((paths) => {
    const { generated } = checkSchema(paths);
    writeFileSync(paths.schemaPath, generated.replace(/\n/g, '\r\n'));
    assert.equal(checkSchema(paths).upToDate, true);
  });
});

test('check fails when schema.d.ts was edited or is missing', () => {
  withFiles((paths) => {
    assert.equal(checkSchema(paths).upToDate, false, 'missing file');

    const { generated } = checkSchema(paths);
    writeFileSync(paths.schemaPath, generated + '// hand edit\n');
    assert.equal(checkSchema(paths).upToDate, false, 'edited file');
  });
});

test('check fails when openapi.json changed but schema.d.ts did not', () => {
  withFiles((paths) => {
    writeFileSync(paths.schemaPath, checkSchema(paths).generated);
    assert.equal(checkSchema(paths).upToDate, true);

    writeFileSync(paths.openapiPath, JSON.stringify({ ...DOCUMENT, paths: { '/api/other': DOCUMENT.paths['/api/ping'] } }));
    assert.equal(checkSchema(paths).upToDate, false);
  });
});
