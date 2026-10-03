// D25: logical properties only. Fails when a template, component or stylesheet uses a physical
// direction utility (ml-, pr-, left-, text-left, rounded-l, border-r, ...) or a physical CSS
// property (margin-left, padding-right, left:, text-align: left, ...). No dependencies.
//
//   node scripts/check-logical-properties.mjs        checks ./src
//   a line containing the comment `logical-ok` is skipped (use sparingly, with a reason).

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, extname } from 'node:path';
import { pathToFileURL } from 'node:url';

const CHECKED_EXTENSIONS = new Set(['.html', '.ts', '.css']);
const SKIPPED_FILE = /\.spec\.ts$/;

// Tailwind utility (after variants and a leading "-" or "!") -> the logical replacement.
const UTILITIES = [
  [/^ml-/, 'ms-'],
  [/^mr-/, 'me-'],
  [/^pl-/, 'ps-'],
  [/^pr-/, 'pe-'],
  [/^left-(\d|\[|\(|px$|full$|auto$)/, 'start-'],
  [/^right-(\d|\[|\(|px$|full$|auto$)/, 'end-'],
  [/^text-left$/, 'text-start'],
  [/^text-right$/, 'text-end'],
  [/^rounded-l(-|$)/, 'rounded-s'],
  [/^rounded-r(-|$)/, 'rounded-e'],
  [/^rounded-tl(-|$)/, 'rounded-ss'],
  [/^rounded-tr(-|$)/, 'rounded-se'],
  [/^rounded-bl(-|$)/, 'rounded-es'],
  [/^rounded-br(-|$)/, 'rounded-ee'],
  [/^border-l(-|$)/, 'border-s'],
  [/^border-r(-|$)/, 'border-e'],
  [/^float-left$/, 'float-start'],
  [/^float-right$/, 'float-end'],
  [/^clear-left$/, 'clear-start'],
  [/^clear-right$/, 'clear-end'],
  [/^scroll-ml-/, 'scroll-ms-'],
  [/^scroll-mr-/, 'scroll-me-'],
  [/^scroll-pl-/, 'scroll-ps-'],
  [/^scroll-pr-/, 'scroll-pe-'],
];

// Physical CSS properties (component styles, inline style attributes).
const CSS_PROPERTIES = [
  [/(?<![\w-])(margin|padding)-(left|right)(?![\w-])/, (m) => `${m[1]}-inline-${m[2] === 'left' ? 'start' : 'end'}`],
  [/(?<![\w-])border-(left|right)(?:-(?:width|style|color))?(?![\w-])/, (m) => `border-inline-${m[1] === 'left' ? 'start' : 'end'}`],
  [/(?<![\w-])border-(top|bottom)-(left|right)-radius(?![\w-])/, () => 'border-start-start-radius and its siblings'],
  [/(?<![\w-])(left|right)\s*:/, (m) => `inset-inline-${m[1] === 'left' ? 'start' : 'end'}`],
  [/(?<![\w-])text-align\s*:\s*(left|right)(?![\w-])/, (m) => `text-align: ${m[1] === 'left' ? 'start' : 'end'}`],
  [/(?<![\w-])float\s*:\s*(left|right)(?![\w-])/, (m) => `float: inline-${m[1] === 'left' ? 'start' : 'end'}`],
  [/(?<![\w-])clear\s*:\s*(left|right)(?![\w-])/, (m) => `clear: inline-${m[1] === 'left' ? 'start' : 'end'}`],
];

function* walk(directory) {
  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) {
      yield* walk(path);
    } else if (CHECKED_EXTENSIONS.has(extname(path)) && !SKIPPED_FILE.test(path)) {
      yield path;
    }
  }
}

function utilityBase(token) {
  // "md:hover:-ml-2" -> "ml-2"; "[&>*]:pr-1" -> "pr-1"; "!ml-2" -> "ml-2"
  const lastVariant = token.lastIndexOf(':');
  let base = lastVariant === -1 ? token : token.slice(lastVariant + 1);
  base = base.replace(/^!/, '').replace(/^-/, '');
  return base;
}

/** Returns [{ file, line, found, use }] for every violation under `root`. */
export function findViolations(root) {
  const violations = [];

  for (const file of walk(root)) {
    const lines = readFileSync(file, 'utf8').split(/\r?\n/);

    lines.forEach((text, index) => {
      if (text.includes('logical-ok')) {
        return;
      }

      const report = (found, use) => violations.push({ file: relative(root, file), line: index + 1, found, use });

      if (extname(file) !== '.css') {
        for (const token of text.split(/[\s"'`<>=(){};,]+/)) {
          const base = utilityBase(token);
          const rule = UTILITIES.find(([pattern]) => pattern.test(base));
          if (rule) {
            report(token, rule[1]);
          }
        }
      }

      for (const [pattern, suggest] of CSS_PROPERTIES) {
        const match = pattern.exec(text);
        if (match && !(extname(file) === '.ts' && !/style|css/i.test(text))) {
          report(match[0].trim(), suggest(match));
        }
      }
    });
  }

  return violations;
}

function main() {
  const root = process.argv[2] ?? 'src';
  const violations = findViolations(root);

  if (violations.length === 0) {
    console.log('check:logical OK: no physical direction utilities or CSS properties.');
    return;
  }

  console.error('check:logical FAILED (D25: logical properties only):');
  for (const { file, line, found, use } of violations) {
    console.error(`  ${join(root, file)}:${line}  "${found}"  ->  use ${use}`);
  }
  process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
