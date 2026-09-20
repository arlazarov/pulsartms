import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const css = compileString(
  "@use 'pages/dispatch/views'; @use 'shared/dispatch/load-dialog'; @use 'pages/dispatch/load';",
  {
    loadPaths: [fileURLToPath(new URL('../../Styles/', import.meta.url))],
  },
).css;

function rule(selector) {
  const matches = [...css.matchAll(/([^{}]+)\{([^{}]+)\}/g)].filter(
    ([, selectors]) => {
      const normalized = selectors.trim().replace(/\s+/g, ' ');
      return (
        normalized === selector || normalized.split(', ').includes(selector)
      );
    },
  );
  assert.ok(matches.length > 0, `Missing style: ${selector}`);
  return matches.map(([, , body]) => body).join('\n');
}

test('Table differentiates current rows while leaving upcoming loads neutral and money aligned', () => {
  assert.match(
    rule('.dispatch-page .dispatch-table tr.is-current td'),
    /background: var\(--ui-selected\);/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table tr.is-planned td'),
    /background: var\(--ui-surface\);/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table .dispatch-table__money'),
    /text-align: right;[\s\S]*font-variant-numeric: tabular-nums;/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table .dispatch-table__money strong'),
    /font-size: var\(--type-body\);[\s\S]*font-weight: 500;/,
  );
  assert.match(
    rule(
      '.dispatch-page .dispatch-table .dispatch-table__mileage-values strong',
    ),
    /font-size: var\(--type-lead\);[\s\S]*font-weight: 700;/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table .dispatch-table__truck'),
    /display: flex;[\s\S]*align-items: center;/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table .dispatch-table__status'),
    /font-size: var\(--type-small\);/,
  );
});

test('Table allocates more width to stops and keeps identity and schedule readable', () => {
  assert.match(rule('.dispatch-page .dispatch-table'), /table-layout: fixed;/);
  const columns = {
    load: 13,
    truck: 15,
    stop: 17,
    miles: 14,
    rate: 10,
    rpm: 7,
  };
  for (const [name, width] of Object.entries(columns)) {
    assert.match(
      rule(`.dispatch-page .dispatch-table .dispatch-table__column-${name}`),
      new RegExp(`width: ${width}%;`),
    );
  }
  assert.equal(
    columns.load +
      columns.truck +
      columns.stop * 2 +
      columns.miles +
      columns.rate +
      columns.rpm * 2,
    100,
  );
  assert.ok(columns.stop * 2 > columns.load + columns.truck);
  assert.match(
    rule(
      '.dispatch-page .dispatch-table .dispatch-table__truck > strong, .dispatch-page .dispatch-table .dispatch-table__equipment > strong, .dispatch-page .dispatch-table .dispatch-table__stop-entry > strong',
    ),
    /font-size: var\(--type-lead\);/,
  );
  assert.match(
    rule(
      '.dispatch-page .dispatch-table .dispatch-table__stop .dispatch-table__schedule',
    ),
    /font-size: var\(--type-body\);/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table .dispatch-table__equipment > span'),
    /font-size: var\(--type-body\);/,
  );
});

test('Table rows offer visible keyboard focus and pointer feedback without expanding inline panels', () => {
  assert.match(rule('.dispatch-table-wrap'), /overflow: auto;/);
  assert.match(
    rule('.dispatch-table-wrap:focus-visible'),
    /outline: 2px solid var\(--ui-focus\);/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table .dispatch-table__row'),
    /cursor: pointer;/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table .dispatch-table__open:focus-visible'),
    /outline: 3px solid var\(--ui-focus\);/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table .dispatch-table__stops-summary'),
    /white-space: normal;/,
  );
  assert.match(
    rule('.dispatch-page .dispatch-table td'),
    /padding-block: var\(--space-md\);/,
  );
  assert.doesNotMatch(css, /\.dispatch-table__load-details/);
});

test('Papers keep folder queues and share a viewport-bounded native load modal', () => {
  assert.match(rule('.dispatch-papers-workspace'), /display: grid;/);
  assert.match(
    rule('.dispatch-papers'),
    /grid-template-columns: repeat\(3,\s*minmax\(0,\s*1fr\)\);/,
  );
  assert.match(
    rule('.dispatch-load-dialog'),
    /position: fixed;[\s\S]*max-height: calc\(100dvh - var\(--space-section\) \* 2\);/,
  );
  assert.match(
    rule('.dispatch-load-dialog[open]'),
    /display: flex;[\s\S]*flex-direction: column;/,
  );
  assert.match(
    rule('.dispatch-load-dialog__content'),
    /min-height: 0;[\s\S]*overflow-y: auto;/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__identity'),
    /position: sticky;[\s\S]*flex-shrink: 0;/,
  );
  assert.match(rule('.dispatch-load-dialog::backdrop'), /background:/);
  assert.match(
    rule('.dispatch-paper__tab:focus-visible'),
    /outline: 2px solid var\(--ui-focus\);/,
  );
  assert.match(
    rule('.dispatch-paper__tab-status'),
    /font-size: var\(--type-small\);/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__title h2'),
    /color: var\(--ui-text\);/,
  );
});

test('programmatic dialog heading focus is quiet while the close control retains a keyboard focus ring', () => {
  assert.match(
    rule(
      '.dispatch-load-dialog .dispatch-paper__identity[tabindex="-1"]:focus-visible',
    ),
    /outline: none;/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__close:focus-visible'),
    /outline: 3px solid var\(--ui-focus\);/,
  );
  assert.doesNotMatch(
    css,
    /\.dispatch-load-dialog(?:\s+\*)?:focus-visible\s*\{\s*outline: none;/,
  );
});

test('Paper financial strip keeps four desktop columns and adapts mobile columns to readable text widths', () => {
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__financials'),
    /grid-template-columns: repeat\(4,\s*minmax\(0,\s*1fr\)\);/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__financials dd'),
    /font-variant-numeric: tabular-nums;/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__financials dd'),
    /font-size: var\(--type-body\);/,
  );
  assert.match(
    rule(
      '.dispatch-load-dialog .dispatch-paper__financials .dispatch-paper__mileage dd',
    ),
    /font-size: var\(--type-heading\);[\s\S]*font-weight: 700;/,
  );
  assert.match(
    css,
    /@media \(width < 800px\)[\s\S]*\.dispatch-paper__financials\s*\{\s*grid-template-columns: repeat\(auto-fit,\s*minmax\(min\(100%,\s*var\(--size-dispatch-dialog-metric\)\),\s*1fr\)\);/,
  );
  assert.match(
    css,
    /@media \(width < 800px\)[\s\S]*\.dispatch-paper__financials > div \+ div\s*\{\s*border-left: 0;/,
  );
});

test('Paper modal reuses the circular semantic stop markers and fills the mobile viewport safely', () => {
  const marker = rule('.dispatch-load__stop-number');
  assert.match(marker, /width: var\(--size-dispatch-stop-marker\);/);
  assert.match(marker, /height: var\(--size-dispatch-stop-marker\);/);
  assert.match(marker, /border-radius: var\(--radius-pill\);/);
  assert.match(
    rule(
      '.dispatch-load-dialog .dispatch-paper__stops .dispatch-load__stop-number',
    ),
    /position: static;[\s\S]*color: var\(--dialog-stop-color\);/,
  );
  assert.match(css, /--dialog-stop-color: var\(--ui-pickup\);/);
  assert.match(css, /--dialog-stop-color: var\(--ui-delivery\);/);
  assert.match(
    css,
    /@media \(width < 800px\)[\s\S]*\.dispatch-load-dialog\s*\{[^}]*width: calc\(100vw - var\(--space-sm\) \* 2\);[^}]*max-height: calc\(100dvh - var\(--space-sm\) \* 2\);/,
  );
});

test('Load modal stays compact with responsive side-by-side stop cards rather than full-width vertical sheets', () => {
  assert.match(
    rule('.dispatch-load-dialog'),
    /width: min\(var\(--size-content-load-dialog\),\s*100vw - var\(--space-section\) \* 2\);/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__stops'),
    /grid-template-columns: repeat\(auto-fit,\s*minmax\(min\(100%,\s*var\(--size-dispatch-load-card\)\),\s*1fr\)\);/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__stops'),
    /align-items: start;[\s\S]*gap: var\(--space-md\);/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__identity'),
    /padding: var\(--space-lg\);/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__title h2'),
    /font-size: var\(--type-lead\);/,
  );
  assert.doesNotMatch(
    rule('.dispatch-load-dialog .dispatch-paper__stops > li'),
    /(?:min-)?height:/,
  );
});

test('modal stop navigation uses shared buttons and retains hidden overview content', () => {
  assert.match(
    rule('.dispatch-load-dialog .dispatch-load__more-details'),
    /justify-self: start;/,
  );
  // The dialog keeps hidden content and lets the app hide it; it no longer
  // repeats that rule - see the style-token checks.
  assert.doesNotMatch(css, /\[hidden\]\s*\{\s*display: none/);
  assert.match(
    rule('.dispatch-load-dialog__content'),
    /overflow-anchor: none;/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-load__stop-detail-content'),
    /border: 0;[\s\S]*background: var\(--ui-surface-soft\);/,
  );
  assert.match(
    rule('.dispatch-load-dialog.is-stop-view .dispatch-paper__stops'),
    /grid-template-columns: minmax\(0,\s*1fr\);/,
  );
  assert.doesNotMatch(
    css,
    /(?:^|\n)\.dispatch-load__stop-details > summary\s*\{/,
  );
});

test('Stop facts wrap within their view and group resting actions in one footer', () => {
  assert.doesNotMatch(
    css,
    /li:has\([^}]*stop-details\[open\]\)[^{]*\{[^}]*grid-column:/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__stop-facts'),
    /display: flex;[\s\S]*flex-wrap: wrap;/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__stop-actions'),
    /display: flex;[\s\S]*flex-wrap: wrap;/,
  );
  assert.match(
    rule('.dispatch-load-dialog .dispatch-paper__stop-metadata'),
    /grid-template-columns: minmax\(0,\s*1fr\);/,
  );
});
