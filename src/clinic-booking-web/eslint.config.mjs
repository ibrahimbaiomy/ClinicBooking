// @ts-check
import { defineConfig } from 'eslint/config';
import tseslint from 'typescript-eslint';
import angular from 'angular-eslint';

export default defineConfig(
  {
    // schema.d.ts is generated (D24) and never hand-edited, so it is not linted.
    ignores: ['dist/**', '.angular/**', 'node_modules/**', 'scripts/**', 'src/api/schema.d.ts'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'cb', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'cb', style: 'kebab-case' },
      ],
      // Instructions.md: strict mode, no `any`, standalone components, inject().
      '@typescript-eslint/no-explicit-any': 'error',
      '@angular-eslint/prefer-inject': 'error',
      '@angular-eslint/prefer-standalone': 'error',
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {},
  },
);
