import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const css = compileString(
  "@use 'pages/fleet-map/stage'; @use 'pages/fleet-map/inspector'; @use 'pages/fleet-map/popup';@use 'pages/fleet-map/station';@use 'shared/fuel/visit';",
  {
    loadPaths: [fileURLToPath(new URL('../../Styles/', import.meta.url))],
  },
).css;

test('the days keep three aligned columns under the prices, and the prices keep their accents', () => {
  assert.match(
    css,
    /\.fleet-station-popup__days\s*\{[^}]*display: grid;[^}]*grid-template-columns: repeat\(3, minmax\(0, 1fr\)\);/,
  );
  // Each day is centred in its equal column, so the gaps either side of
  // today's rim are equal (the owner, September 27).
  assert.match(
    css,
    /\.fleet-station-popup__day\s*\{[^}]*display: flex;[^}]*flex-direction: column;[^}]*align-items: center;[^}]*white-space: nowrap;/,
  );
  // Today is marked as an instrument: an accent rim and tint, not a
  // solid block (the owner, September 27).
  assert.match(
    css,
    /\.fleet-station-popup__day\.is-current\s*\{[^}]*border-color: color-mix\(in srgb, var\(--ui-accent\)/,
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

test('an ordinary fuel quote keeps its own reading inside the one card box', () => {
  // The quote no longer sizes the card (the owner, September 28: the
  // panel's edges must not move between choices); its content still reads
  // as one column.
  assert.doesNotMatch(css, /width: fit-content;/);
  assert.match(
    css,
    /\.fleet-map-inspector\[data-inspector-mode=fuel\]:not\(:has\(\.fleet-station-popup--planned\)\) \.fleet-map-inspector__native\s*\{[^}]*container-type: normal;/,
  );
  assert.match(
    css,
    /\.fleet-station-popup:not\(\.fleet-station-popup--planned\)\s*\{[^}]*display: block;/,
  );
});

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

test('a stop, a next stop and a station use the one card box', () => {
  // No card sizes itself by what it shows: the stage owns one box
  // (the owner, September 28).
  assert.doesNotMatch(css, /var\(--size-map-stop-inspector\)/);
  assert.doesNotMatch(css, /min-height: min\(/);
  // The days stand under the prices they are about, which puts them in the
  // half of the card that is about the place - by being inside it, not by
  // being sent to a column.
  assert.match(
    css,
    /\.fleet-station-popup--planned > \.fleet-station-popup__place\s*\{[^}]*padding-right: var\(--space-lg\);/,
  );
});
