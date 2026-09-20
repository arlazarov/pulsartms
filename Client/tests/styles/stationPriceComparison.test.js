import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const css = compileString(
  "@use 'pages/fleet-map/details'; @use 'pages/fleet-map/popup-content';",
  {
    loadPaths: [fileURLToPath(new URL('../../Styles/', import.meta.url))],
  },
).css;

// The days are one line under the price list: three cells of one height,
// today set apart by a quiet ground, and no rules between them. A table of
// every price by day was a grid of rules with rows of two heights.
test('the days are one even line under the prices, and the prices keep their accents', () => {
  assert.match(
    css,
    /\.fleet-station-popup__days\s*\{[^}]*display: grid;[^}]*grid-auto-flow: column;[^}]*grid-auto-columns: minmax\(0, 1fr\);/,
  );
  assert.match(
    css,
    /\.fleet-station-popup__day\.is-current\s*\{[^}]*background: var\(--ui-surface-soft\);/,
  );
  assert.doesNotMatch(css, /\.fleet-station-popup__comparison (table|td|th)/);
  assert.match(
    css,
    /\.fleet-station-popup__discount-label,\s*\.fleet-station-popup__discount\s*\{[^}]*color: var\(--ui-link\);[^}]*background: var\(--ui-selected\);/,
  );
  assert.match(
    css,
    /\.fleet-station-popup__discount,\s*\.fleet-station-popup__ifta\s*\{[^}]*color: var\(--ui-success-text\);/,
  );
  assert.match(
    css,
    /\.fleet-station-popup__savings-label,\s*\.fleet-station-popup__savings\s*\{[^}]*color: var\(--ui-success-text\);/,
  );
});

test('ordinary fuel inspector fits its quote without changing truck or planned fuel layout', () => {
  assert.match(css, /min-width: min\(100%, var\(--size-map-fuel-quote\)\);/);
  assert.match(
    css,
    /\.fleet-map-inspector\[data-inspector-mode=fuel\]:not\(:has\(\.fleet-station-popup--planned\)\)\s*\{[^}]*width: fit-content;[^}]*max-width: min\(100%,\s*var\(--size-map-fuel-card\)\);/,
  );
  assert.match(
    css,
    /\.fleet-map-inspector\[data-inspector-mode=fuel\]:not\(:has\(\.fleet-station-popup--planned\)\) \.fleet-map-inspector__native\s*\{[^}]*container-type: normal;/,
  );
  assert.match(
    css,
    /\.fleet-map-inspector\[data-inspector-mode=fuel\]:not\(:has\(\.fleet-station-popup--planned\)\) \.fleet-station-popup\s*\{[^}]*display: block;/,
  );
});

// Drawn, the list and the line of days kept the same two edges. Built, the
// list was as narrow as its longest name while the days ran the width of the
// card, and the band behind the price being paid was a name and a figure with
// a gap between them.
test('the price list keeps the edges the days keep, and its band is one band', () => {
  assert.match(
    css,
    /\.fleet-station-popup__prices\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\) auto;[^}]*gap: var\(--space-micro\) 0;/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-station-popup__prices\s*\{[^}]*justify-content: start;/,
  );
  assert.match(
    css,
    /\.fleet-station-popup__discount-label\s*\{[^}]*margin-inline-start: calc\(-1 \* var\(--space-xs\)\);/,
  );
  assert.match(
    css,
    /\.fleet-station-popup__discount\s*\{[^}]*margin-inline-end: calc\(-1 \* var\(--space-xs\)\);/,
  );
});

test('a price that fell is green and one that rose is not', () => {
  assert.match(
    css,
    /\.fleet-station-popup__change\.is-decrease\s*\{[^}]*color: var\(--ui-success-text\);/,
  );
  assert.match(
    css,
    /\.fleet-station-popup__change\.is-increase\s*\{[^}]*color: var\(--ui-telemetry-critical-icon\);/,
  );
});

test('single-day quotes retain the ordinary fuel inspector sizing', () => {
  assert.doesNotMatch(
    css,
    /:not\(:has\(\.fleet-station-popup__comparison:not\(\[hidden\]\)\)\)/,
  );
});

test('a planned fuel stop keeps its bounded width, and the days stay with the prices', () => {
  assert.match(css, /width: min\(100%, var\(--size-map-fuel-inspector\)\);/);
  // The days stand under the prices they are about, in the half of the card
  // that is about the place - not in a column of their own.
  assert.match(
    css,
    /\.fleet-station-popup--planned > \.fleet-station-popup__address,[^{]*\.fleet-station-popup--planned > \.fleet-station-popup__comparison\s*\{[^}]*grid-column: 1;/,
  );
});

test('current and future route stop inspectors share a compact bounded width', () => {
  assert.match(
    css,
    /\.fleet-map-inspector\[data-inspector-mode=stop\],\s*\.fleet-map-inspector\[data-inspector-mode=nextstop\]\s*\{[^}]*width: min\(100%,\s*var\(--size-map-stop-inspector\)\);/,
  );
});
