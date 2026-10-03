// D24: API types come from the committed openapi.json and are never hand-edited.
//
//   npm run gen:api     refreshes openapi.json from the API (through the OpenApiDocumentTests
//                       regeneration switch, needs the .NET SDK), then regenerates schema.d.ts
//   npm run check:api   regenerates schema.d.ts in memory from the committed openapi.json and fails
//                       if the committed schema.d.ts differs (Node only; for CI)
//
// Works the same in PowerShell, Git Bash and Linux: no shell syntax, the environment variable is set
// from Node, and the generator is started through `node`, not a .cmd shim.

import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = resolve(webRoot, '..', '..');
const defaults = {
  openapiPath: join(webRoot, 'src', 'api', 'openapi.json'),
  schemaPath: join(webRoot, 'src', 'api', 'schema.d.ts'),
};
const generatorCli = join(webRoot, 'node_modules', 'openapi-typescript', 'bin', 'cli.js');

// LF endings and one final newline, so the file is identical on every platform.
const normalize = (text) => text.replace(/\r\n/g, '\n').trimEnd() + '\n';

/** Runs openapi-typescript on `openapiPath` and returns the normalised TypeScript text. */
export function generateSchemaText(openapiPath) {
  const directory = mkdtempSync(join(tmpdir(), 'gen-api-'));
  const output = join(directory, 'schema.d.ts');
  try {
    const result = spawnSync(process.execPath, [generatorCli, openapiPath, '-o', output], {
      encoding: 'utf8',
    });
    if (result.status !== 0) {
      throw new Error(`openapi-typescript failed:\n${result.stdout}${result.stderr}`);
    }
    return normalize(readFileSync(output, 'utf8'));
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

/** @returns {{ upToDate: boolean, generated: string }} */
export function checkSchema({ openapiPath, schemaPath } = defaults) {
  const generated = generateSchemaText(openapiPath);
  const committed = existsSync(schemaPath) ? normalize(readFileSync(schemaPath, 'utf8')) : null;
  return { upToDate: committed === generated, generated };
}

function refreshOpenApiDocument() {
  console.log('Refreshing openapi.json from the API (UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests)...');
  const result = spawnSync(
    'dotnet',
    ['test', join(repoRoot, 'ClinicBooking.sln'), '--filter', 'FullyQualifiedName~OpenApiDocumentTests', '--nologo'],
    { cwd: repoRoot, env: { ...process.env, UPDATE_OPENAPI: '1' }, stdio: 'inherit' },
  );

  if (result.error || result.status !== 0) {
    console.error('gen:api FAILED: could not regenerate openapi.json (is the .NET SDK installed?).');
    process.exit(1);
  }
}

function main() {
  if (process.argv.includes('--check')) {
    const { upToDate } = checkSchema();
    if (!upToDate) {
      console.error('check:api FAILED: schema.d.ts is out of date. Run `npm run gen:api` and commit openapi.json and schema.d.ts.');
      process.exit(1);
    }
    console.log('check:api OK: schema.d.ts matches openapi.json.');
    return;
  }

  refreshOpenApiDocument();
  const { generated } = checkSchema();
  writeFileSync(defaults.schemaPath, generated);
  console.log('gen:api OK: openapi.json and schema.d.ts are up to date. Commit both.');
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
