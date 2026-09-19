import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'pages';", { loadPaths }).css;

function rule(selector) {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = css.match(new RegExp(`(?:^|\\n)${escaped}\\s*\\{([^}]+)\\}`));
  assert.ok(match, `Missing style: ${selector}`);
  return match[1];
}

test('application page cards share neutral surfaces and compact semantic corners', () => {
  for (const selector of [
    '.settings-page__card',
    '.add-user-page form',
    '.login-page .container',
    '.dispatch-truck',
    '.dispatch-load',
    '.dispatch-table-wrap',
    '.fleet-map-truck-info',
    '.fleet-map-details-card',
  ]) {
    const body = rule(selector);
    assert.match(body, /background: var\(--ui-surface\);/, selector);
    assert.match(body, /border-radius: var\(--radius-sm\);/, selector);
    assert.match(
      body,
      /border: 1px solid var\(--ui-border-subtle\);/,
      selector,
    );
  }
});

test('current dispatch stays recognizable without tinting the entire load card', () => {
  const body = rule('.dispatch-load--current');
  assert.match(body, /border-color: var\(--ui-link\);/);
  assert.match(body, /box-shadow: 0 0 0 1px var\(--ui-link\);/);
  assert.doesNotMatch(body, /background:/);
  assert.match(
    rule('.dispatch-load--current .dispatch-load__phase'),
    /background: var\(--ui-selected\);/,
  );
});

test('account forms stay content-sized and their actions can wrap on small screens', () => {
  assert.match(
    rule('.add-user-page form'),
    /max-width: var\(--size-content-account\);/,
  );
  assert.match(
    rule('.login-page .container'),
    /max-width: var\(--size-content-login\);/,
  );
  assert.match(rule('.add-user-page .form__actions'), /flex-wrap: wrap;/);
  assert.match(
    css,
    /@media \(max-width: 799px\)[\s\S]*\.add-user-page \.form__actions > \.btn\s*\{\s*flex: 1 1 auto;/,
  );
});

test('paper tabs retain keyboard focus and table statuses retain their meaning', () => {
  assert.match(
    rule('.dispatch-paper__tab:focus-visible'),
    /outline: 2px solid var\(--ui-focus\);/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table tr.is-planned td'),
    /background: var\(--ui-surface\);/,
  );
  assert.match(
    rule('.dispatch-load__status.is-moving'),
    /color: var\(--ui-success-text\);/,
  );
});
test('dispatch scope wraps and Papers uses native full-page load links', () => {
  const board = readFileSync(
    new URL('../../Styles/pages/dispatch/_board.scss', import.meta.url),
    'utf8',
  );
  const papers = readFileSync(
    new URL('../../Pages/Dispatch/DispatchPapers.razor', import.meta.url),
    'utf8',
  );
  const dialog = readFileSync(
    new URL(
      '../../Shared/Dispatch/DispatchLoadDialog/DispatchLoadDialog.razor',
      import.meta.url,
    ),
    'utf8',
  );
  assert.match(
    board,
    /&__scope,\s*&__filters &__scope\s*\{\s*display: inline-flex;/,
  );
  assert.match(
    board,
    /&__scope,\s*&__filters &__scope\s*\{[^}]*flex-wrap: wrap;[^}]*min-width: 0;\s*max-width: 100%;/,
  );
  const link = papers.match(/<a\b[^>]*class="dispatch-paper__tab"[^>]*>/s)?.[0];
  assert.ok(link);
  assert.match(link, /href="@\(\$"\/dispatch\/\{row\.Load\.Id\}"\)"/);
  assert.doesNotMatch(link, /@onclick|role="button"/);
  assert.doesNotMatch(papers, /<DispatchLoadDialog\b/);
  assert.doesNotMatch(papers, /dispatch-paper--pulled|<details\b/);
  assert.match(dialog, /<dialog\b[^>]*aria-labelledby=/);
});
