import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const compile = source => compileString(source, { loadPaths }).css;

test('settings surfaces pair their background with themed text', () => {
  const css = compile("@use 'pages/settings-page';");
  assert.match(css, /\.settings-page\s*\{[^}]*color: var\(--ui-text\);/);
  const card = css.match(/^\.settings-page__card\s*\{([^}]+)\}/m)[1];
  assert.match(card, /background: var\(--ui-surface\);/);
  assert.match(card, /color: var\(--ui-text\);/);
});

test('shared foundation preserves control and touch dimensions independently of editor widths', () => {
  assert.doesNotThrow(() =>
    compile(`@use 'sass:map'; @use 'base/variables' as v;
    @if map.get(v.$sizes, control) != 2.5rem { @error 'Standard control changed'; }
    @if map.get(v.$sizes, control-touch) != 2.75rem { @error 'Touch control changed'; }
    @if map.get(v.$radii, sm) != .5rem { @error 'Control radius changed'; }
    @if map.get(v.$sizes, fuel-editor) != 46rem { @error 'Editor width changed'; }
    @if map.get(v.$sizes, fuel-editor-route) != 20rem { @error 'Timeline width changed'; }
    @if map.get(v.$sizes, content-account) != 36rem { @error 'Account form width changed'; }
    @if map.get(v.$sizes, content-login) != 25rem { @error 'Sign-in width changed'; }`),
  );
});

test('input hover does not increase specificity over error or focus states', () => {
  const css = compile("@use 'components/form-fields';");
  assert.match(css, /input:where\(:hover:not\(:disabled\):not\(:focus\)\)/);
  assert.match(
    css,
    /input\.invalid,[\s\S]*?input\[aria-invalid=true\][\s\S]*?border-color: var\(--ui-danger-text\);/,
  );
  assert.match(
    css,
    /\.form-field__control--error input,[\s\S]*?border-color: var\(--ui-danger-text\);/,
  );
  assert.match(
    css,
    /\.form-field__control input:focus,[\s\S]*?border-color: var\(--ui-focus\);/,
  );
});

test('quiet table rows keep keyboard selection and destructive action affordances', () => {
  const css = compile(
    "@use 'components/data-table'; @use 'components/buttons';",
  );
  assert.match(
    css,
    /\.data-table__header\s*\{\s*background: var\(--ui-surface-soft\);\s*color: var\(--ui-text-secondary\);/,
  );
  assert.match(
    css,
    /\.data-table__row:focus-within\s*\{\s*background: var\(--ui-selected\);/,
  );
  assert.match(
    css,
    /\.btn--table\s*\{\s*--button-bg: transparent;\s*--button-border: transparent;/,
  );
  assert.match(
    css,
    /\.btn--table-danger\s*\{\s*--button-text: var\(--ui-danger-text\);/,
  );
  assert.match(
    css,
    /\.btn:focus-visible\s*\{\s*outline: 3px solid var\(--ui-focus\);/,
  );
});

test('selected toolbar view uses filled action colors without changing native map toggles', () => {
  const css = compile("@use 'components/filter-toolbar';");
  assert.match(
    css,
    /\.filter-toolbar__views button\[aria-pressed=true\]\s*\{[^}]*--button-hover-bg: var\(--ui-action-hover\);[^}]*--button-hover-text: var\(--ui-on-accent\);[^}]*background: var\(--ui-action\);\s*border-color: var\(--ui-action\);\s*color: var\(--ui-on-accent\);/,
  );
  assert.match(
    css,
    /\.filter-toolbar__toggle\s*\{\s*--button-bg: transparent;/,
  );
  assert.doesNotMatch(css, /box-shadow|appearance:\s*none/);
});

test('form and dialog actions wrap instead of clipping when text grows', () => {
  const css = compile("@use 'components/form'; @use 'components/popup';");
  for (const selector of ['form__actions', 'popup__footer']) {
    assert.match(
      css,
      new RegExp(
        `\\.${selector}\\s*\\{[^}]*flex-wrap: wrap;[^}]*border-top: 1px solid var\\(--ui-border-subtle\\);`,
        's',
      ),
    );
  }
  assert.match(
    css,
    /\.form__errors\s*\{[^}]*color: var\(--ui-danger-text\);\s*font-size: var\(--type-label\);/s,
  );
});

test('page titles keep the same control-height row with or without a description', () => {
  const css = compile("@use 'components/page-header';");
  assert.match(
    css,
    /\.page-header h1\s*\{\s*display: flex;\s*align-items: center;\s*min-height: var\(--size-control\);/,
  );
  assert.match(
    css,
    /@media \(max-width: 799px\)[\s\S]*?\.page-header h1\s*\{\s*min-height: var\(--size-control-touch\);/,
  );
});
