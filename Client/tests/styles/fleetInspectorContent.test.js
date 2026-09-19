import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'pages/fleet-map/popup-content';", {
  loadPaths,
}).css;

test('docked fuel details keep compact bounded gauges without changing standalone cards', () => {
  assert.match(
    css,
    /\.fleet-map-inspector__native \.fleet-station-popup--planned \.fleet-fuel-visit__levels\s*\{[^}]*max-width: var\(--size-fuel-levels\);/,
  );
  assert.match(
    css,
    /\.fleet-map-inspector__native \.fleet-station-popup--planned \.fleet-fuel-visit__dial\s*\{[^}]*--hos-dial-size: var\(--size-fuel-dial\);/,
  );
});

test('wide docked fuel details place station, quote and purchases beside each other', () => {
  assert.match(
    css,
    /\.fleet-map-inspector__native \.fleet-station-popup--planned\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\) max-content minmax\(0, 1fr\);/,
  );
  assert.match(
    css,
    /\.fleet-map-inspector__native \.fleet-station-popup--planned > \.fleet-station-popup__prices\s*\{[^}]*grid-column: 2;[^}]*grid-row: 1\s*\/\s*span 4;/,
  );
  assert.match(
    css,
    /\.fleet-map-inspector__native \.fleet-station-popup--planned > \.fleet-station-popup__visits\s*\{[^}]*grid-column: 3;[^}]*grid-row: 1\s*\/\s*span 4;/,
  );
});

test('narrow native fuel levels adapt to available width and enlarged text', () => {
  assert.match(
    css,
    /\.fleet-map-inspector__native\s*\{\s*container: map-inspector\s*\/\s*inline-size;/,
  );
  assert.match(css, /@container map-inspector \(width < 20rem\)/);
  assert.match(
    css,
    /\.fleet-map-inspector__native \.fleet-fuel-visit__levels\s*\{\s*grid-template-columns: repeat\(2, minmax\(0, 1fr\)\);/,
  );
  assert.match(
    css,
    /\.fleet-map-inspector__native \.fleet-fuel-visit__action\s*\{[^}]*grid-row: 2;[^}]*flex-wrap: wrap;/,
  );
});
