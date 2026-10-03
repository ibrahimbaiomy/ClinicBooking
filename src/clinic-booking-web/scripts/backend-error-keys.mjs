// Finds the `error.*` keys the .NET back end can send, by reading its C# source (D26, D52).
// Plain Node, no .NET and no package. Used by check-i18n.mjs.
//
// What counts as a key: a string literal in src/ClinicBooking.*/**/*.cs (never tests/) that has the
// strict shape `error.<reason>` or `error.<area>.<reason>` (lower-case letters, digits, underscores). Comments are
// skipped by a small scanner that understands C# strings, so `//` inside "http://x" is not a
// comment and a key mentioned only in a comment is not a key.
//
// What it cannot see: a key built at runtime. The one such family today is `error.http.<status>`.
// Any file that contains a prefix-only literal ("error." or "error.http.") must be declared in
// backend-error-keys.json with the keys it can produce, or the check fails; a declared file that is
// gone or no longer has a prefix literal is reported as stale.

import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const KEY = /^error(?:\.[a-z0-9_]+)+$/;
const PREFIX = /^error(?:\.[a-z0-9_]+)*\.$/;
const SKIPPED_DIRECTORIES = new Set(['bin', 'obj', 'Migrations', 'node_modules']);

/** All string literals of a C# file, comments removed. @returns {{ value: string, line: number }[]} */
export function extractCSharpStrings(source) {
  const strings = [];
  const length = source.length;
  let line = 1;
  let i = 0;

  const advance = (count = 1) => {
    for (let n = 0; n < count; n++) {
      if (source[i] === '\n') {
        line++;
      }
      i++;
    }
  };

  while (i < length) {
    const ch = source[i];
    const next = source[i + 1];

    if (ch === '/' && next === '/') {
      while (i < length && source[i] !== '\n') {
        i++;
      }
    } else if (ch === '/' && next === '*') {
      advance(2);
      while (i < length && !(source[i] === '*' && source[i + 1] === '/')) {
        advance();
      }
      advance(2);
    } else if (ch === "'") {
      // char literal: '\'' and '"' must not start a string
      i++;
      while (i < length && source[i] !== "'" && source[i] !== '\n') {
        i += source[i] === '\\' ? 2 : 1;
      }
      i++;
    } else if (ch === '"' || ((ch === '$' || ch === '@') && /^[$@]{1,2}"/.test(source.slice(i, i + 3)))) {
      const start = line;
      let prefixLength = 0;
      while (source[i + prefixLength] === '$' || source[i + prefixLength] === '@') {
        prefixLength++;
      }
      const verbatim = source.slice(i, i + prefixLength).includes('@');
      advance(prefixLength);

      if (source.startsWith('"""', i)) {
        let quotes = 0;
        while (source[i + quotes] === '"') {
          quotes++;
        }
        advance(quotes);
        const closing = '"'.repeat(quotes);
        const end = source.indexOf(closing, i);
        const stop = end === -1 ? length : end;
        strings.push({ value: source.slice(i, stop).trim(), line: start });
        advance(stop - i + (end === -1 ? 0 : quotes));
        continue;
      }

      advance(); // opening quote
      let value = '';
      while (i < length) {
        if (verbatim) {
          if (source[i] === '"' && source[i + 1] === '"') {
            value += '"';
            advance(2);
            continue;
          }
          if (source[i] === '"') {
            break;
          }
        } else {
          if (source[i] === '\\') {
            value += source[i + 1] ?? '';
            advance(2);
            continue;
          }
          if (source[i] === '"' || source[i] === '\n') {
            break;
          }
        }
        value += source[i];
        advance();
      }
      advance(); // closing quote
      strings.push({ value, line: start });
    } else {
      advance();
    }
  }

  return strings;
}

function* csharpFiles(directory) {
  for (const entry of readdirSync(directory).sort()) {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) {
      if (!SKIPPED_DIRECTORIES.has(entry)) {
        yield* csharpFiles(path);
      }
    } else if (entry.endsWith('.cs')) {
      yield path;
    }
  }
}

/**
 * @param {{ sourceDirectory: string, repositoryRoot: string, config: { dynamicSources?: { file: string, reason?: string, keys: string[] }[] }, requireSources: boolean }} options
 * @returns {{ keys: { key: string, where: string }[], errors: string[], warnings: string[] }}
 */
export function scanBackendErrorKeys({ sourceDirectory, repositoryRoot, config, requireSources }) {
  const keys = [];
  const errors = [];
  const warnings = [];
  const projects = existsSync(sourceDirectory)
    ? readdirSync(sourceDirectory).filter((name) => name.startsWith('ClinicBooking.') && statSync(join(sourceDirectory, name)).isDirectory())
    : [];

  if (projects.length === 0) {
    const message = `back-end sources not found under ${sourceDirectory}; back-end error keys were not checked`;
    (requireSources ? errors : warnings).push(requireSources ? `${message} (CI must run this check where the sources exist)` : message);
    return { keys, errors, warnings };
  }

  const declared = new Map((config.dynamicSources ?? []).map((source) => [source.file, source]));
  const seenPrefixFiles = new Set();

  for (const project of projects) {
    for (const path of csharpFiles(join(sourceDirectory, project))) {
      const file = relative(repositoryRoot, path).split('\\').join('/');
      const literals = extractCSharpStrings(readFileSync(path, 'utf8').replace(/\r\n/g, '\n'));

      for (const { value, line } of literals) {
        if (KEY.test(value)) {
          keys.push({ key: value, where: `${file}:${line}` });
        } else if (PREFIX.test(value)) {
          seenPrefixFiles.add(file);
          if (!declared.has(file)) {
            errors.push(
              `${file}:${line}: "${value}" builds keys at runtime that cannot be read from source; declare the file and the keys it can produce in scripts/backend-error-keys.json`,
            );
          }
        }
      }
    }
  }

  for (const [file, source] of declared) {
    if (!seenPrefixFiles.has(file)) {
      errors.push(`scripts/backend-error-keys.json: "${file}" is stale (missing, or it no longer builds keys from an "error." prefix); remove or update the entry`);
      continue;
    }
    for (const key of source.keys) {
      if (!KEY.test(key)) {
        errors.push(`scripts/backend-error-keys.json: "${key}" for ${file} is not a well-formed error key`);
      } else {
        keys.push({ key, where: `${file} (declared in scripts/backend-error-keys.json)` });
      }
    }
  }

  return { keys, errors, warnings };
}
