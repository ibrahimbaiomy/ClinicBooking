// D26: a missing or mismatched translation is a build failure, never silently untranslated text.
// No dependencies. Run from the web project folder:  node scripts/check-i18n.mjs [root]
//
// Verifies
//   1. every folder under public/i18n has ar.json and en.json with the same keys, the same
//      {{placeholders}} per key, and non-empty string values;
//   2. every translation key used in templates and code exists (root files or a scope folder);
//   3. component templates contain no literal user-facing text (heuristic, see limits);
//   4. components use templateUrl, so no template can hide from (3);
//   5. every `error.*` key in the back-end C# source has a translation in both languages.
//
// Limits (also in Instructions.md)
//   - A key built at runtime cannot be verified. Such a line must carry the comment
//     `i18n-keys: a.b, c.d` (same or previous line) listing every key it can produce; those are verified.
//   - Literal strings inside .ts code are not detected (review and lint cover them).
//   - Text detection is a heuristic on the template source, not a parse of the compiled template.
//   - Back-end `error.*` keys are read from the C# source (backend-error-keys.mjs): a key built at runtime
//     must be declared in scripts/backend-error-keys.json. Keys assembled any other way (a resource file,
//     a database) would be invisible. Without the back-end sources the scan is skipped with a warning,
//     unless CI is set, where it fails.
//   - Unused keys are reported as warnings only.

import { readdirSync, readFileSync, statSync, existsSync } from 'node:fs';
import { join, relative, extname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { scanBackendErrorKeys } from './backend-error-keys.mjs';

const LANGS = ['ar', 'en'];
const IGNORED_UNUSED_PREFIXES = ['error.'];
const USER_FACING_ATTRIBUTES = ['title', 'alt', 'placeholder', 'aria-label', 'aria-description', 'aria-placeholder', 'label', 'summary'];

// ---- helpers ------------------------------------------------------------------------------

function* walk(directory) {
  for (const entry of readdirSync(directory).sort()) {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) {
      yield* walk(path);
    } else {
      yield path;
    }
  }
}

const blank = (text) => text.replace(/[^\n]/g, ' ');
const lineOf = (content, index) => content.slice(0, index).split('\n').length;

function flatten(value, prefix, out, problems, file) {
  for (const [key, child] of Object.entries(value)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (child !== null && typeof child === 'object' && !Array.isArray(child)) {
      flatten(child, path, out, problems, file);
    } else if (typeof child === 'string') {
      if (child.trim() === '') {
        problems.push(`${file}: "${path}" is empty`);
      }
      out.set(path, child);
    } else {
      problems.push(`${file}: "${path}" must be a string`);
    }
  }
}

const placeholders = (text) => [...text.matchAll(/\{\{\s*([\w.$]+)\s*\}\}/g)].map((m) => m[1]).sort().join(',');

// ---- 1. translation files ----------------------------------------------------------------

function loadTranslations(i18nDirectory, errors) {
  /** @type {{ scope: string, prefix: string, ar: Map<string,string>, en: Map<string,string> }[]} */
  const scopes = [];
  if (!existsSync(i18nDirectory)) {
    errors.push(`${i18nDirectory} does not exist`);
    return scopes;
  }

  const directories = new Set([i18nDirectory]);
  for (const file of walk(i18nDirectory)) {
    if (extname(file) === '.json') {
      directories.add(join(file, '..'));
    }
  }

  for (const directory of [...directories].sort()) {
    const scope = relative(i18nDirectory, directory).split('\\').join('/');
    const entry = { scope, prefix: scope.split('/').join('.'), ar: new Map(), en: new Map() };

    for (const language of LANGS) {
      const file = join(directory, `${language}.json`);
      const label = relative(i18nDirectory, file).split('\\').join('/');
      if (!existsSync(file)) {
        errors.push(`i18n/${label} is missing`);
        continue;
      }

      try {
        const problems = [];
        flatten(JSON.parse(readFileSync(file, 'utf8')), '', entry[language], problems, `i18n/${label}`);
        errors.push(...problems);
      } catch (error) {
        errors.push(`i18n/${label} is not valid JSON: ${error.message}`);
      }
    }

    for (const language of LANGS) {
      const other = language === 'ar' ? 'en' : 'ar';
      for (const key of entry[language].keys()) {
        if (!entry[other].has(key)) {
          errors.push(`i18n/${scope ? scope + '/' : ''}${other}.json is missing "${key}" (present in ${language}.json)`);
        }
      }
    }

    for (const [key, value] of entry.ar) {
      if (entry.en.has(key) && placeholders(value) !== placeholders(entry.en.get(key))) {
        errors.push(`i18n/${scope ? scope + '/' : ''}: "${key}" has different {{placeholders}} in ar.json and en.json`);
      }
    }

    scopes.push(entry);
  }

  return scopes;
}

function keyExists(scopes, language, key) {
  return scopes.some(({ prefix, ...maps }) => {
    if (prefix === '') {
      return maps[language].has(key);
    }
    return key.startsWith(prefix + '.') && maps[language].has(key.slice(prefix.length + 1));
  });
}

// ---- 2. keys used in sources -------------------------------------------------------------

function markerKeys(lines, index) {
  for (const text of [lines[index], lines[index - 1]]) {
    const match = text && /i18n-keys:\s*([^\n]*?)\s*(?:-->|\*\/|$)/.exec(text);
    if (match) {
      return match[1].split(/[,\s]+/).filter(Boolean);
    }
  }
  return null;
}

function collectUsedKeys(file, content, used, errors) {
  const lines = content.split('\n');
  const isTemplate = file.endsWith('.html');
  const structural = isTemplate && /\*transloco\s*=/.test(content);

  lines.forEach((text, index) => {
    const where = `${file}:${index + 1}`;
    const literals = [];
    let totalCalls = 0;

    if (isTemplate) {
      totalCalls += (text.match(/\|\s*transloco\b/g) ?? []).length;
      for (const m of text.matchAll(/(['"])([A-Za-z0-9_.\-]+)\1\s*\|\s*transloco\b/g)) {
        literals.push(m[2]);
      }
      if (structural) {
        totalCalls += (text.match(/(?<![\w.])t\(/g) ?? []).length;
        for (const m of text.matchAll(/(?<![\w.])t\(\s*(['"])([A-Za-z0-9_.\-]+)\1/g)) {
          literals.push(m[2]);
        }
      }
    } else {
      totalCalls += (text.match(/\.(?:translate|selectTranslate|translateObject|selectTranslateObject)\(/g) ?? []).length;
      for (const m of text.matchAll(/\.(?:translate|selectTranslate|translateObject|selectTranslateObject)\(\s*(['"`])((?:(?!\1)[^\\])*)\1/g)) {
        if (!m[2].includes('${')) {
          literals.push(m[2]);
        }
      }
    }

    literals.forEach((key) => used.push({ key, where }));

    if (totalCalls > literals.length) {
      const keys = markerKeys(lines, index);
      if (keys) {
        keys.forEach((key) => used.push({ key, where }));
      } else {
        errors.push(`${where}: translation key is not a string literal; add a comment "i18n-keys: a.b, c.d" listing every key it can be`);
      }
    }
  });
}

// ---- 3. literal text in templates --------------------------------------------------------

function blankControlFlow(content) {
  let out = '';
  let i = 0;
  while (i < content.length) {
    const rest = content.slice(i);
    const letMatch = /^@let\b[^;]*;/.exec(rest);
    if (letMatch) {
      out += blank(letMatch[0]);
      i += letMatch[0].length;
      continue;
    }

    const keyword = /^@(?:if|else if|else|for|switch|case|default|empty|defer|placeholder|loading|error)\b/.exec(rest);
    if (keyword) {
      let j = keyword[0].length;
      while (/\s/.test(content[i + j] ?? '')) {
        j++;
      }
      if (content[i + j] === '(') {
        let depth = 0;
        let quote = null;
        for (; i + j < content.length; j++) {
          const ch = content[i + j];
          if (quote) {
            quote = ch === quote ? null : quote;
          } else if (ch === '"' || ch === "'") {
            quote = ch;
          } else if (ch === '(') {
            depth++;
          } else if (ch === ')' && --depth === 0) {
            j++;
            break;
          }
        }
      }
      while (/\s/.test(content[i + j] ?? '')) {
        j++;
      }
      if (content[i + j] === '{') {
        j++;
      }
      out += blank(content.slice(i, i + j));
      i += j;
      continue;
    }

    out += content[i];
    i++;
  }
  return out.replace(/[{}]/g, ' ');
}

function findLiteralText(file, source, errors) {
  let content = source
    .replace(/<!--[\s\S]*?-->/g, blank)
    .replace(/<(script|style)\b[\s\S]*?<\/\1>/gi, blank);

  // attributes
  for (const tag of content.matchAll(/<[a-zA-Z][^>]*>/g)) {
    for (const attribute of USER_FACING_ATTRIBUTES) {
      const literal = new RegExp(`\\s${attribute}\\s*=\\s*"([^"]*)"`, 'g');
      for (const m of tag[0].matchAll(literal)) {
        if (/\p{L}/u.test(m[1].replace(/\{\{[\s\S]*?\}\}/g, ''))) {
          errors.push(`${file}:${lineOf(content, tag.index)}: literal ${attribute}="${m[1]}"; use a translation key`);
        }
      }

      const bound = new RegExp(`\\[(?:attr\\.)?${attribute}\\]\\s*=\\s*"([^"]*)"`, 'g');
      for (const m of tag[0].matchAll(bound)) {
        if (/(['"])[^'"]*\p{L}[^'"]*\1/u.test(m[1]) && !/\|\s*transloco\b/.test(m[1])) {
          errors.push(`${file}:${lineOf(content, tag.index)}: [${attribute}] binds a literal string; use a translation key`);
        }
      }
    }
  }

  content = content.replace(/<[^>]*>/g, blank);

  // interpolations
  for (const m of content.matchAll(/\{\{([\s\S]*?)\}\}/g)) {
    // `| intl: 'date'` passes a format name, not text for the user (D27).
    const expression = m[1].replace(/\|\s*intl\s*:\s*(['"])[A-Za-z]+\1/g, '');
    if (/(['"])[^'"]*\p{L}[^'"]*\1/u.test(expression) && !/\|\s*transloco\b/.test(expression)) {
      errors.push(`${file}:${lineOf(content, m.index)}: interpolation contains a literal string; use a translation key`);
    }
  }
  content = content.replace(/\{\{[\s\S]*?\}\}/g, blank);

  // text nodes
  content = blankControlFlow(content);
  content.split('\n').forEach((text, index) => {
    const match = /\p{L}{2,}/u.exec(text);
    if (match) {
      errors.push(`${file}:${index + 1}: literal text "${text.trim()}"; use a translation key`);
    }
  });
}

// ---- main check --------------------------------------------------------------------------

/** @returns {{ errors: string[], warnings: string[] }} */
export function checkI18n(root, options = {}) {
  const errors = [];
  const warnings = [];
  const scopes = loadTranslations(join(root, 'public', 'i18n'), errors);

  const used = [];
  const sourceRoot = join(root, 'src');
  for (const path of existsSync(sourceRoot) ? walk(sourceRoot) : []) {
    const extension = extname(path);
    const file = relative(root, path).split('\\').join('/');
    if ((extension !== '.html' && extension !== '.ts') || file.endsWith('.spec.ts') || file === 'src/index.html') {
      continue;
    }

    const content = readFileSync(path, 'utf8').replace(/\r\n/g, '\n');
    collectUsedKeys(file, content, used, errors);

    if (extension === '.html') {
      findLiteralText(file, content, errors);
    } else {
      const inline = /@Component\s*\(\s*\{[\s\S]*?\btemplate\s*:/.exec(content);
      if (inline) {
        errors.push(`${file}:${lineOf(content, inline.index)}: inline template; use templateUrl so the translation check can read it`);
      }
    }
  }

  for (const { key, where } of used) {
    for (const language of LANGS) {
      if (!keyExists(scopes, language, key)) {
        errors.push(`${where}: key "${key}" is missing from ${language}.json`);
      }
    }
  }

  if (options.backend) {
    const backend = scanBackendErrorKeys(options.backend);
    errors.push(...backend.errors);
    warnings.push(...backend.warnings);
    for (const { key, where } of backend.keys) {
      for (const language of LANGS) {
        if (!keyExists(scopes, language, key)) {
          errors.push(`${where}: back-end key "${key}" is missing from ${language}.json`);
        }
      }
    }
  }

  const usedKeys = new Set(used.map((u) => u.key));
  for (const { prefix, ar } of scopes) {
    for (const key of ar.keys()) {
      const full = prefix ? `${prefix}.${key}` : key;
      if (!usedKeys.has(full) && !IGNORED_UNUSED_PREFIXES.some((p) => full.startsWith(p))) {
        warnings.push(`unused key "${full}"`);
      }
    }
  }

  return { errors: [...new Set(errors)], warnings };
}

function main() {
  const root = process.argv[2] ?? process.cwd();
  const repositoryRoot = join(root, '..', '..');
  const config = JSON.parse(readFileSync(join(fileURLToPath(new URL('.', import.meta.url)), 'backend-error-keys.json'), 'utf8'));
  const { errors, warnings } = checkI18n(root, {
    backend: { sourceDirectory: join(repositoryRoot, 'src'), repositoryRoot, config, requireSources: Boolean(process.env.CI) },
  });

  warnings.forEach((w) => console.warn(`  warning: ${w}`));

  if (errors.length === 0) {
    console.log('check:i18n OK: ar.json and en.json match, every used key and back-end error key exists, no literal text in templates.');
    return;
  }

  console.error('check:i18n FAILED (D26):');
  errors.forEach((e) => console.error(`  ${e}`));
  process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
