import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'pages/fleet-map/popup-content';", {
  loadPaths,
}).css;

// A planned fuel stop, in the truck card's language: two halves under one
// head - the place on the left, the visit on the right - with the actions
// under both. It was three unequal columns, two dials and an arrow, the price
// said twice an inch apart, and a filled button no other card uses.
test('a planned fuel stop is two halves: the place, and the visit', () => {
  assert.match(
    css,
    /\.fleet-station-popup--planned\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\) minmax\(0, 1fr\);/,
  );
  for (const part of ['address', 'prices', 'comparison'])
    assert.match(
      css,
      new RegExp(
        `\\.fleet-station-popup--planned > \\.fleet-station-popup__${part}[^{]*\\{[^}]*grid-column: 1;`,
      ),
    );
  assert.match(
    css,
    /\.fleet-station-popup--planned > \.fleet-station-popup__visits:not\(\[hidden\]\)\s*\{[^}]*grid-column: 2;[^}]*border-left: 1px solid/,
  );
  // The head of the visit: which stop it is, and what is left to it.
  assert.match(
    css,
    /\.fleet-station-popup--planned > \.fleet-station-popup__plan-label\s*\{[^}]*grid-column: 2;[^}]*grid-row: 1;[^}]*justify-self: start;/,
  );
  assert.match(
    css,
    /\.fleet-station-popup--planned > \.fleet-station-popup__distance:not\(\[hidden\]\)\s*\{[^}]*grid-column: 2;[^}]*grid-row: 1;[^}]*justify-self: end;/,
  );
  assert.match(
    css,
    /\.fleet-station-popup--planned > \.fleet-station-popup__actions:not\(\[hidden\]\)\s*\{[^}]*grid-column: 1\s*\/\s*-1;[^}]*justify-content: flex-start;/,
  );
});

// The tank is one bar - what is in it on arrival, and what the stop adds -
// and the fill is facts on one label column. The last dials on the map went
// with this.
test('the tank is one bar and the fill is facts on one label column', () => {
  assert.match(
    css,
    /\.fleet-fuel-visit__tank\s*\{[^}]*display: flex;[^}]*overflow: hidden;/,
  );
  assert.match(css, /\.fleet-fuel-visit__tank-add\s*\{[^}]*opacity: 0\.35;/);
  assert.match(
    css,
    /\.fleet-fuel-visit__facts\s*\{[^}]*grid-template-columns: max-content minmax\(0, 1fr\);/,
  );
  assert.match(
    css,
    /\.fleet-fuel-visit__fact\s*\{[^}]*grid-template-columns: subgrid;/,
  );
  assert.doesNotMatch(css, /fleet-fuel-visit__(dial|gauge|levels|arrow|buy)/);
});

test('too narrow for two halves, the visit goes under the place and the head still reads first', () => {
  assert.match(
    css,
    /\.fleet-map-inspector__native\s*\{\s*container: map-inspector\s*\/\s*inline-size;/,
  );
  const narrow = css.slice(
    css.indexOf('@container map-inspector (width < 34rem)'),
  );
  assert.ok(narrow.length > 0 && narrow.length < css.length);
  assert.match(
    narrow,
    /\.fleet-station-popup--planned\s*\{\s*grid-template-columns: minmax\(0, 1fr\);/,
  );
  assert.match(narrow, /__title\s*\{\s*order: -3;/);
  assert.match(narrow, /__plan-label\s*\{\s*order: -2;/);
  assert.match(narrow, /__distance:not\(\[hidden\]\)\s*\{[^}]*order: -1;/);
});
