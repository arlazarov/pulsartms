import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const css = compileString("@use 'pages/settings-page';", {
  loadPaths: [fileURLToPath(new URL('../../Styles/', import.meta.url))],
}).css;

function rule(selector) {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const found = css.match(new RegExp(`(?:^|\\n)${escaped}\\s*\\{([^}]+)\\}`));
  assert.ok(found, `Missing style: ${selector}`);
  return found[1];
}

test('integration cards use three bounded columns and stack on narrow screens', () => {
  assert.match(
    rule('.integration-settings__grid'),
    /grid-template-columns: repeat\(3, minmax\(0, 1fr\)\);/,
  );
  assert.match(rule('.settings-page__card'), /min-width: 0;/);
  assert.match(
    css,
    /@media \(max-width: 999px\)[\s\S]*\.integration-settings__grid\s*\{\s*grid-template-columns: minmax\(0, 1fr\);/,
  );
  assert.match(rule('.integration-settings__card-heading'), /flex-wrap: wrap;/);
  assert.match(
    rule('.integration-settings .settings-page__fields'),
    /grid-template-columns: minmax\(0, 1fr\);/,
  );
});

test('integration statuses use semantic colors and fields retain shared control sizing', () => {
  assert.match(
    rule('.integration-settings__status.is-configured'),
    /background: var\(--ui-success-surface\);/,
  );
  assert.match(
    rule('.integration-settings__status.is-configured'),
    /color: var\(--ui-success-text\);/,
  );
  assert.match(
    rule('.settings-page__fields input, .settings-page__fields select'),
    /min-height: var\(--size-control\);/,
  );
  assert.match(
    rule('.settings-page__fields input, .settings-page__fields select'),
    /width: 100%;/,
  );
});
