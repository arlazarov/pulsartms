import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'pages/dispatch/stop-workspace';", {
  loadPaths,
}).css;

const declarations = selector => {
  const rule = [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].find(([, keys]) =>
    keys.split(',').some(key => key.trim() === selector),
  );
  assert.ok(rule, `Missing style rule: ${selector}`);
  return rule[2];
};

test('stop rows align their facts and wrap full resource names', () => {
  assert.match(css, /container: stop-workspace\s*\/\s*inline-size;/);
  const row = declarations('.stop-workspace__select');
  assert.match(
    row,
    /grid-template-columns: var\(--size-dispatch-stop-marker\) minmax\(0, 2.1fr\)/,
  );
  assert.match(row, /min-height: var\(--size-control-touch\);/);
  assert.match(row, /align-items: start;/);
  const resources = declarations('.stop-workspace__assignment-preview');
  assert.match(resources, /overflow-wrap: anywhere;/);
  assert.match(resources, /font-size: var\(--type-small\);/);
  assert.doesNotMatch(resources, /ellipsis|nowrap|hidden|display: none/);
  assert.match(css, /@container stop-workspace \(max-width: 65rem\)/);
  assert.match(css, /@container stop-editor \(max-width: 22rem\)/);
});

test('medium rows keep one accessible reorder handle beside their facts', () => {
  const order = declarations('.stop-workspace__order');
  assert.match(order, /display: grid;/);
  assert.match(order, /grid-template-columns: auto;/);
  assert.match(declarations('.stop-workspace__drag'), /cursor: grab;/);
  assert.doesNotMatch(order, /position: absolute;/);
  assert.match(
    declarations(
      '.stop-workspace__stop.is-drop-before > .stop-workspace__drop-marker',
    ),
    /border-top: 3px solid;/,
  );
  assert.match(
    declarations(
      '.stop-workspace__stop.is-drop-after > .stop-workspace__drop-marker',
    ),
    /border-bottom: 3px solid;/,
  );
});

test('the stop identity gets priority over the compact ETA column', () => {
  assert.match(
    declarations('.stop-workspace__row'),
    /grid-template-columns: minmax\(0, 4.6fr\) minmax\(0, 0.65fr\)/,
  );
  assert.match(declarations('.stop-workspace__add-button'), /min-height:/);
});

test('itinerary and details scroll independently within one viewport-sized workspace', () => {
  const list = declarations('.stop-workspace__list');
  assert.match(
    list,
    /max-block-size: min\(30%, var\(--size-dispatch-stop-list-compact\)\);/,
  );
  assert.match(list, /overflow-y: auto;/);
  assert.match(list, /display: block;/);
  assert.match(
    declarations('.stop-workspace'),
    /grid-template-columns: minmax\(0, 2.4fr\) minmax\(0, 1fr\);/,
  );
  assert.match(list, /scrollbar-gutter: stable;/);
  assert.doesNotMatch(
    declarations('.stop-workspace__details'),
    /max-block-size|overflow-y/,
  );
  assert.match(declarations('.stop-workspace'), /block-size: 76dvh;/);
  assert.match(declarations('.stop-workspace__editor'), /overflow-y: auto;/);
  assert.match(declarations('.stop-workspace__details'), /min-block-size: 0;/);
});

test('editor density preserves shared control sizes', () => {
  assert.match(
    declarations('.stop-workspace__fields'),
    /grid-template-columns: repeat\(2, minmax\(0, 1fr\)\);/,
  );
  assert.match(
    css,
    /\.stop-workspace__resources\s*\{\s*display: flex;\s*flex-wrap: wrap;/,
  );
  const times = declarations('.stop-workspace__appointment-times');
  assert.equal(
    times.match(/grid-template-columns: ([^;]+);/)?.[1],
    'repeat(auto-fit, minmax(min(100%, 9rem), 1fr))',
  );
  assert.match(
    declarations('.stop-workspace__editor'),
    /gap: var\(--space-md\);/,
  );
  assert.match(
    declarations('.stop-workspace input'),
    /min-height: var\(--size-control\);/,
  );
  const mobile = css.slice(css.indexOf('@media (max-width: 799px)'));
  assert.match(mobile, /min-height: var\(--size-control-touch\);/);
  assert.doesNotMatch(
    declarations('.stop-workspace__details'),
    /height:|position:|overflow: hidden|display: none/,
  );
});
